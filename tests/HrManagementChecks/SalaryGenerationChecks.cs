using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Vertex_ERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class SalaryGenerationChecks
{
    public static async Task Run(ApplicationDbContext db, Action<bool, string> check)
    {
        var first = await db.Employees.SingleAsync();
        var user = await db.AppUsers.SingleAsync();
        var second = new Employee { EmployeeCode = "PAY2", FirstName = "Second", FullName = "Second Employee", Email = "second@example.invalid", PhoneNumber = "7000000099", Department = first.Department, DepartmentId = first.DepartmentId, Designation = "Tester", JoiningDate = first.JoiningDate };
        db.EmployeeSalaryDetails.Add(new EmployeeSalaryDetail { Employee = second, BasicSalary = 12000, IsActive = true });
        await db.SaveChangesAsync();
        var previous = DateTime.Today.AddMonths(-1);
        HrController Controller(ApplicationDbContext context, Dictionary<string, StringValues>? values = null)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }, "test")) };
            http.Request.Form = new FormCollection(values ?? new());
            return new HrController(context, null!, NullLogger<HrController>.Instance, null!) { ControllerContext = new() { HttpContext = http }, TempData = new TempDataDictionary(http, new MemoryTempData()) };
        }
        Dictionary<string, StringValues> Values(string days = "22.5") => new()
        {
            [$"salaryDays_{first.Id}"] = days, [$"leaveDeduction_{first.Id}"] = "100.50", [$"deductionNote_{first.Id}"] = "First only",
            [$"salaryDays_{second.Id}"] = "18", [$"leaveDeduction_{second.Id}"] = "350", [$"deductionNote_{second.Id}"] = "Second only"
        };
        foreach (var date in new[] { DateTime.Today, DateTime.Today.AddMonths(1), DateTime.Today.AddMonths(-2) })
        {
            await Controller(db, Values()).GenerateSalarySlips(date.Year, date.Month, new() { first.Id }, null);
            check(!await db.GeneratedSalarySlips.AnyAsync(), "Reject generation outside previous month: " + date.ToString("yyyy-MM"));
        }
        foreach (var invalid in new[] { "", "-1", "32", "abc", "1.234" })
        {
            await Controller(db, Values(invalid)).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id }, null);
            check(!await db.GeneratedSalarySlips.AnyAsync(), "Invalid salary days rejected: " + invalid);
        }
        await Controller(db, Values()).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id }, "Other department");
        check(!await db.GeneratedSalarySlips.AnyAsync(), "Employee outside department selection rejected");
        await Controller(db, Values()).GenerateSalarySlips(previous.Year, previous.Month, new() { second.Id }, null, first.Id);
        check(!await db.GeneratedSalarySlips.AnyAsync(), "Employee outside individual selection rejected");
        foreach (var invalid in new[] { "-1", "bad", "999999", "1.234" })
        {
            var values = Values();
            values[$"leaveDeduction_{second.Id}"] = invalid;
            await Controller(db, values).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id, second.Id }, null);
            check(!await db.GeneratedSalarySlips.AnyAsync(), "Invalid deduction rejects entire selection without partial generation: " + invalid);
        }
        var page = (SalarySlipAdminViewModel)((ViewResult)await Controller(db).SalarySlips(null, null, null, first.Id)).Model!;
        check(page.Year == previous.Year && page.Month == previous.Month && page.CanGenerate && page.Employees.Count == 1 && page.EmployeeOptions.Count == 2, "Previous month default and independent employee filter options");
        await Controller(db, Values()).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id, second.Id }, first.Department, null, first.Id);
        check(await db.GeneratedSalarySlips.CountAsync() == 1, "Individual generation changes only selected employee");
        await Controller(db, Values()).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id, second.Id }, first.Department);
        db.ChangeTracker.Clear();
        var slips = await db.GeneratedSalarySlips.OrderBy(x => x.EmployeeId).ToListAsync();
        check(slips.Count == 2 && slips[0].SalaryDays == 22.5m && slips[1].SalaryDays == 18 && slips[0].LeaveDeduction == 100.5m && slips[1].LeaveDeduction == 350 && slips[0].DeductionNote == "First only" && slips[1].DeductionNote == "Second only", "Department generation preserves separate employee days and deductions");
        var timestamp = slips[0].GeneratedAtUtc;
        await Controller(db, Values("1")).GenerateSalarySlips(previous.Year, previous.Month, new() { first.Id }, null);
        db.ChangeTracker.Clear();
        check(await db.GeneratedSalarySlips.CountAsync() == 2 && (await db.GeneratedSalarySlips.SingleAsync(x => x.EmployeeId == first.Id)).SalaryDays == 22.5m && (await db.GeneratedSalarySlips.SingleAsync(x => x.EmployeeId == first.Id)).GeneratedAtUtc == timestamp, "Repeat generation cannot overwrite a slip");
        var salary = await db.EmployeeSalaryDetails.SingleAsync(x => x.EmployeeId == first.Id);
        salary.BasicSalary = 19000;
        await db.SaveChangesAsync();
        page = (SalarySlipAdminViewModel)((ViewResult)await Controller(db).SalarySlips(previous.Year, previous.Month, null, first.Id)).Model!;
        check(page.Employees.Single().GrossSalary == slips[0].GrossSalary, "Generated screen keeps original salary snapshot after salary changes");
        var pdf = SalarySlipPdfService.Create(first, slips[0], null);
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        check(text.Contains("(Salary Days)") && text.Contains("(22.5)"), "PDF includes saved salary days");
        var output = Path.Combine(Path.GetTempPath(), "vertex-salary-days-check.pdf");
        await File.WriteAllBytesAsync(output, pdf);
        Console.WriteLine("PDF preview: " + output);
        db.GeneratedSalarySlips.Add(new GeneratedSalarySlip { EmployeeId = first.Id, Year = previous.Year, Month = previous.Month, GeneratedByUserId = user.Id });
        try { await db.SaveChangesAsync(); throw new Exception("Duplicate salary slip unexpectedly saved"); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { check(true, "Database unique index prevents duplicate employee/month slips"); }
        db.ChangeTracker.Clear();
        await SalaryRevisionChecks.Run(db, first.Id, user.Id, check);
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
