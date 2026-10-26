using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
var roles = typeof(SalarySlipReviewController).GetCustomAttribute<AuthorizeAttribute>()?.Roles;
Check(roles == "Admin,HR", "Review and preview are limited to Admin/HR");
var post = typeof(SalarySlipReviewController).GetMethods().Single(m => m.Name == "Edit" && m.GetCustomAttribute<HttpPostAttribute>() != null);
Check(post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, "Generation requires an antiforgery token");
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
var controller = new SalarySlipReviewController(db);
Check(await controller.Edit(1, 2000, 1, null) is BadRequestObjectResult, "Old periods rejected before querying employee data");
Check(await controller.Edit(new SalarySlipReviewViewModel { Year = 2000, Month = 1 }, "generate") is BadRequestObjectResult, "POST cannot generate outside the previous month");
Check(await controller.PreviewDraft(1, 2000, 1, 30) is BadRequestObjectResult, "Draft preview validates period");
var employee = new Employee { FullName = "Sample Employee", EmployeeCode = "VPC0121", CompanyCode = "VPC", Department = "Accounts & Finance", Designation = "Accounts Executive", JoiningDate = new DateOnly(2023, 1, 10) };
var slip = new GeneratedSalarySlip { Year = 2026, Month = 9, SalaryDays = 28, BasicSalary = 25000, HouseRentAllowance = 10000, ConveyanceAllowance = 1600, SpecialAllowance = 3400, ProvidentFund = 1800, ProfessionalTax = 200, LeaveDeduction = 500 };
var output = args[0]; Directory.CreateDirectory(output);
var power = SalarySlipPdfService.Create(employee, slip, null);
File.WriteAllBytes(Path.Combine(output, "power-preview.pdf"), power);
Check(Encoding.ASCII.GetString(power).Contains("37,500.00"), "Preview contains the calculated net pay");
Check(!Encoding.ASCII.GetString(power).Contains("0 0 597 80 re W n"), "Power PDF excludes the letterhead footer");
var header = File.ReadAllBytes("DocumentTemplates/Power-Header.jpg");
Check(power.AsSpan().IndexOf(header) >= 0, "Power PDF embeds the supplied replacement letterhead");
employee.CompanyCode = "VAS"; employee.EmployeeCode = "VAS0146";
var automation = SalarySlipPdfService.Create(employee, slip, null);
File.WriteAllBytes(Path.Combine(output, "automation-preview.pdf"), automation);
Check(automation.AsSpan().IndexOf(File.ReadAllBytes("DocumentTemplates/Automation-Letterhead.jpg")) >= 0, "Automation retains its own letterhead");
Check(automation.AsSpan().IndexOf(header) < 0, "Power letterhead is not used for Automation");

var monthly = new GeneratedSalarySlip { Year = 2026, Month = 9, BasicSalary = 18000, HouseRentAllowance = 9000, ConveyanceAllowance = 1500, SpecialAllowance = 1500, SalaryDays = 20 };
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
Check(monthly.GrossSalary == 20000, "20 of 30 days pays exactly two-thirds of monthly earnings");
monthly.SalaryDays = 15;
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
Check(monthly.GrossSalary == 15000, "Repeated days updates reuse monthly baseline without compounding");
monthly.SalaryDays = 30;
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
Check(monthly.GrossSalary == 30000, "Full month restores original earnings");
var revised = new GeneratedSalarySlip { Year = 2026, Month = 9, SalaryDays = 27, BasicSalary = 15000, ProvidentFund = 100 };
SalaryDayCalculation.Apply(revised, SalaryDayCalculation.MonthlyEarnings(revised));
var revisedValues = SalaryDayCalculation.EditingValues(revised);
revisedValues["BasicSalary"] = 20000;
SalaryDayCalculation.Apply(revised, revisedValues);
Check(revised.BasicSalary == 18000 && revised.NetSalary == 17900, "Revised monthly earnings recalculate actual earnings and net pay");
var freshPdf = Encoding.ASCII.GetString(SalarySlipPdfService.Create(employee, revised, null));
Check(freshPdf.Contains("20,000.00") && freshPdf.Contains("18,000.00") && freshPdf.Contains("17,900.00"), "Updated PDF contains matching master, actual and net pay");
revised.BasicSalary = 13500;
var refreshedPdf = Encoding.ASCII.GetString(SalarySlipPdfService.Create(employee, revised, null));
Check(refreshedPdf.Contains("18,000.00") && refreshedPdf.Contains("17,900.00") && revised.BasicSalary == 13500, "PDF recalculates stale actual amounts from saved baseline without mutating stored entity");
monthly.SalaryDays = 0;
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
Check(monthly.GrossSalary == 0, "Zero salary days produces zero earnings");
monthly.Year = 2024; monthly.Month = 2; monthly.SalaryDays = 14.5m;
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
Check(monthly.GrossSalary == 15000, "Leap February and half days use actual calendar days");
monthly.SalaryDays = 30;
try { SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly)); throw new Exception("Invalid days accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: Days exceeding month length rejected"); }
Check(SalaryDayCalculation.CanGenerate(2026, 9, new DateTime(2026, 10, 31)) && !SalaryDayCalculation.CanGenerate(2026, 9, new DateTime(2026, 11, 1)), "Generation expires when the month changes");
Check(SalaryDayCalculation.CanGenerate(2026, 12, new DateTime(2027, 1, 1)) && !SalaryDayCalculation.CanGenerate(2027, 1, new DateTime(2027, 1, 1)), "Only previous month allowed across year boundary");
monthly.Year = 2026; monthly.Month = 9; monthly.SalaryDays = 20;
SalaryDayCalculation.Apply(monthly, SalaryDayCalculation.MonthlyEarnings(monthly));
employee.CompanyCode = "VPC";
File.WriteAllBytes(Path.Combine(output, "power-prorated.pdf"), SalarySlipPdfService.Create(employee, monthly, null));

PdfSharp.Fonts.GlobalFontSettings.FontResolver = new EmployeeDocumentFontResolver("DocumentTemplates");
var letters = new CompanyLetterPdfService(Directory.GetCurrentDirectory());
foreach (var type in CompanyLetterPdfService.DocumentTypes)
{
    var model = new EmployeeDocumentFormViewModel { CompanyCode = "VPC", EmployeeId = 1, EmployeeCode = "VPC0121", EmployeeName = "Sample Employee", DocumentType = type, Email = "sample@example.com", Mobile = "9999999999", Designation = "Engineer", Department = "Engineering", ManagerName = "Sample Manager", JoiningDate = new DateOnly(2024, 1, 1), EffectiveDate = new DateOnly(2026, 10, 1), BasicSalary = 25000, HouseRentAllowance = 10000 };
    var bytes = letters.Build(model);
    File.WriteAllBytes(Path.Combine(output, type.Replace(" / ", "-").Replace(" ", "-") + ".pdf"), bytes);
    Check(bytes.Length > 1000, "Power letter renders: " + type);
}
