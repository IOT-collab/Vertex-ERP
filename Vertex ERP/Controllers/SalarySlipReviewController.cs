using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SalarySlipReviewController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Preview(int id)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().Include(s => s.Employee).SingleOrDefaultAsync(s => s.Id == id);
        return slip == null ? NotFound() : await Pdf(slip);
    }

    [HttpGet]
    public async Task<IActionResult> PreviewDraft(int employeeId, int year, int month, decimal? salaryDays, decimal leaveDeduction = 0, string? deductionNote = null)
    {
        if (!ValidPeriod(year, month)) return BadRequest("Only the previous month's slip can be previewed.");
        var existing = await db.GeneratedSalarySlips.AsNoTracking().Include(s => s.Employee)
            .SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.Year == year && s.Month == month);
        if (existing != null) return await Pdf(existing);
        var slip = await Draft(employeeId, year, month);
        if (slip == null) return NotFound();
        if (!salaryDays.HasValue) return BadRequest("Enter salary days before previewing.");
        slip.SalaryDays = salaryDays; slip.LeaveDeduction = leaveDeduction;
        return await Edit(new SalarySlipReviewViewModel { EmployeeId = employeeId, Year = year, Month = month,
            Values = SalarySlipRevisionViewModel.Snapshot(slip), DeductionNote = deductionNote }, "preview");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int employeeId, int year, int month, decimal? salaryDays, decimal leaveDeduction = 0, string? deductionNote = null)
    {
        if (!ValidPeriod(year, month)) return BadRequest("Only the previous month's ungenerated slip can be reviewed.");
        var existing = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.Year == year && s.Month == month);
        if (existing != null) return RedirectToAction("Edit", "SalarySlipRevision", new { id = existing.Id });
        var slip = await Draft(employeeId, year, month);
        if (slip == null) return NotFound();
        slip.SalaryDays = salaryDays;
        slip.LeaveDeduction = leaveDeduction;
        var values = SalarySlipRevisionViewModel.Snapshot(slip);
        if (!salaryDays.HasValue) values.Remove("SalaryDays");
        return View(new SalarySlipReviewViewModel { EmployeeId = employeeId, Year = year, Month = month,
            EmployeeName = slip.Employee.FullName + " (" + slip.Employee.EmployeeCode + ")",
            Values = values, DeductionNote = deductionNote });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SalarySlipReviewViewModel model, string operation = "preview")
    {
        if (!ValidPeriod(model.Year, model.Month)) return BadRequest("Only the previous month's ungenerated slip can be reviewed.");
        var slip = await Draft(model.EmployeeId, model.Year, model.Month);
        if (slip == null) return NotFound();
        model.EmployeeName = slip.Employee.FullName + " (" + slip.Employee.EmployeeCode + ")";
        foreach (var field in SalarySlipRevisionViewModel.Fields)
        {
            var max = field == "SalaryDays" ? DateTime.DaysInMonth(model.Year, model.Month) : 100000000m;
            if (!model.Values.TryGetValue(field, out var value) || value < 0 || value > max || decimal.Round(value, 2) != value)
                ModelState.AddModelError("Values[" + field + "]", $"Enter {field} between 0 and {max}, with up to two decimal places.");
            else typeof(GeneratedSalarySlip).GetProperty(field)!.SetValue(slip, value);
        }
        if (ModelState.IsValid) SalaryDayCalculation.Apply(slip, model.Values);
        if (slip.TotalDeductions > slip.GrossSalary) ModelState.AddModelError("", "Total deductions cannot exceed gross salary.");
        if (model.DeductionNote?.Length > 300) ModelState.AddModelError(nameof(model.DeductionNote), "Deduction note cannot exceed 300 characters.");
        if (!ModelState.IsValid) return View("Edit", model);
        slip.DeductionNote = model.DeductionNote?.Trim();
        if (operation == "preview") return await Pdf(slip);
        if (operation != "generate") return BadRequest();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !await db.AppUsers.AnyAsync(u => u.Id == userId && u.IsActive)) return Forbid();
        if (await db.GeneratedSalarySlips.AnyAsync(s => s.EmployeeId == model.EmployeeId && s.Year == model.Year && s.Month == model.Month))
        {
            ModelState.AddModelError("", "This slip has already been generated. Return to Salary Slips and use Update.");
            return View(model);
        }
        slip.GeneratedByUserId = userId;
        // Only the reviewed monthly snapshot is saved; salary master data is unchanged.
        slip.Employee = null!;
        db.GeneratedSalarySlips.Add(slip);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            ModelState.AddModelError("", "Another request generated this slip. Return to Salary Slips and use Update.");
            return View(model);
        }
        TempData["SalaryMessage"] = "Reviewed salary slip generated. The employee can now download it.";
        return RedirectToAction("SalarySlips", "Hr", new { year = model.Year, month = model.Month, employeeId = model.EmployeeId });
    }

    private static bool ValidPeriod(int year, int month) => SalaryDayCalculation.CanGenerate(year, month);

    private async Task<GeneratedSalarySlip?> Draft(int employeeId, int year, int month)
    {
        var salary = await db.EmployeeSalaryDetails.AsNoTracking().Include(s => s.Employee)
            .SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.IsActive && s.Employee.IsActive);
        if (salary == null) return null;
        var start = new DateOnly(year, month, 1); var end = start.AddMonths(1).AddDays(-1);
        var leaves = await db.LeaveRequests.AsNoTracking().Where(l => l.EmployeeId == employeeId && !l.IsExtraFacility && l.Status == "Approved" && l.FromDate <= end && l.ToDate >= start).ToListAsync();
        return new GeneratedSalarySlip { EmployeeId = employeeId, Employee = salary.Employee, Year = year, Month = month,
            BasicSalary = salary.BasicSalary, HouseRentAllowance = salary.HouseRentAllowance,
            ConveyanceAllowance = salary.ConveyanceAllowance, SpecialAllowance = salary.SpecialAllowance,
            ProvidentFund = salary.ProvidentFund, ProfessionalTax = salary.ProfessionalTax, Tds = salary.Tds,
            OtherDeductions = salary.OtherDeductions, PfNumber = salary.PfNumber, PfUan = salary.PfUan,
            ApprovedLeaveDays = leaves.Sum(l => ((decimal)(l.ToDate < end ? l.ToDate : end).DayNumber - (l.FromDate > start ? l.FromDate : start).DayNumber + 1) * (l.IsHalfDay ? .5m : 1m)) };
    }

    private async Task<IActionResult> Pdf(GeneratedSalarySlip slip)
    {
        var bank = await db.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(b => b.EmployeeId == slip.EmployeeId);
        return File(SalarySlipPdfService.Create(slip.Employee, slip, bank), "application/pdf");
    }
}
