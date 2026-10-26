using VertexERP.Services;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SalarySlipRevisionController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (slip == null) return NotFound();
        return View("Edit", await Populate(new SalarySlipRevisionViewModel { Id = id, Version = slip.UpdatedAtUtc.Ticks,
            Values = SalaryDayCalculation.EditingValues(slip), DeductionNote = slip.DeductionNote }, slip));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SalarySlipRevisionViewModel model, bool preview = false)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.Id == model.Id);
        if (slip == null) return NotFound();
        foreach (var field in SalarySlipRevisionViewModel.Fields)
        {
            var max = field == "SalaryDays" ? DateTime.DaysInMonth(slip.Year, slip.Month) : 100000000m;
            if (!model.Values.TryGetValue(field, out var value) || value < 0 || value > max || decimal.Round(value, 2) != value)
                ModelState.AddModelError("Values[" + field + "]", $"Enter {field} between 0 and {max}, with up to two decimal places.");
        }
        if (preview) ModelState.Remove(nameof(model.Reason));
        else if (string.IsNullOrWhiteSpace(model.Reason)) ModelState.AddModelError(nameof(model.Reason), "A correction reason is required.");
        GeneratedSalarySlip? calculated = null;
        if (ModelState.IsValid)
        {
            calculated = new GeneratedSalarySlip { Year = slip.Year, Month = slip.Month };
            foreach (var field in SalarySlipRevisionViewModel.Fields)
                typeof(GeneratedSalarySlip).GetProperty(field)!.SetValue(calculated, model.Values[field]);
            SalaryDayCalculation.Apply(calculated, model.Values);
            if (calculated.TotalDeductions > calculated.GrossSalary)
                ModelState.AddModelError("", "Deductions exceed salary for the entered days. Adjust deductions before saving.");
        }
        if (!ModelState.IsValid) return View("Edit", await Populate(model, slip));
        if (preview)
        {
            foreach (var field in SalarySlipRevisionViewModel.Fields)
                typeof(GeneratedSalarySlip).GetProperty(field)!.SetValue(slip, typeof(GeneratedSalarySlip).GetProperty(field)!.GetValue(calculated!));
            slip.MonthlyEarningsJson = calculated!.MonthlyEarningsJson;
            var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == slip.EmployeeId);
            var bank = await db.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(b => b.EmployeeId == slip.EmployeeId);
            Response.Headers.CacheControl = "no-store";
            return File(VertexERP.Services.SalarySlipPdfService.Create(employee, slip, bank), "application/pdf");
        }
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(actor, out var userId) || !await db.AppUsers.AnyAsync(u => u.Id == userId && u.IsActive)) return Forbid();
        var saved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retried transaction must reload the original persisted snapshot.
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync();
            // Lock this slip only; simultaneous corrections must not silently overwrite one another.
            var current = await db.GeneratedSalarySlips.FromSqlInterpolated($"SELECT * FROM \"GeneratedSalarySlips\" WHERE \"Id\" = {model.Id} FOR UPDATE").AsNoTracking().SingleAsync();
            if (current.UpdatedAtUtc.Ticks != model.Version) return false;
            var before = SalarySlipRevisionViewModel.Snapshot(current);
            var previousNote = current.DeductionNote;
            db.Attach(current);
            foreach (var field in SalarySlipRevisionViewModel.Fields)
                typeof(GeneratedSalarySlip).GetProperty(field)!.SetValue(current, typeof(GeneratedSalarySlip).GetProperty(field)!.GetValue(calculated!));
            current.MonthlyEarningsJson = calculated!.MonthlyEarningsJson;
            current.DeductionNote = model.DeductionNote?.Trim();
            current.UpdatedAtUtc = DateTime.UtcNow;
            db.AuditLogs.Add(new AuditLog { EntityType = "SalarySlipRevision:" + current.Id, Action = "Salary slip revised",
                ActorId = actor!, ActorName = User.Identity?.Name ?? actor!, ActorRole = User.FindFirstValue(ClaimTypes.Role) ?? "",
                Detail = JsonSerializer.Serialize(new { Reason = model.Reason.Trim(), Before = before, After = SalarySlipRevisionViewModel.Snapshot(current),
                    PreviousNote = previousNote, Note = current.DeductionNote }) });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        });
        if (!saved)
        {
            ModelState.AddModelError("", "This slip was revised by another request. Reload the page before making your correction.");
            return View("Edit", await Populate(model, slip));
        }
        TempData["SalaryMessage"] = "Salary slip corrected. The employee can download the updated PDF.";
        return RedirectToAction("SalarySlips", "Hr", new { year = slip.Year, month = slip.Month, employeeId = slip.EmployeeId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDays(int id, long version, decimal? salaryDays)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (slip == null) return NotFound();
        if (!ModelState.IsValid || !salaryDays.HasValue || salaryDays < 0 || salaryDays > DateTime.DaysInMonth(slip.Year, slip.Month)
            || decimal.Round(salaryDays.Value, 2) != salaryDays)
        {
            TempData["SalaryError"] = "Enter valid salary days before saving.";
            return RedirectToAction("SalarySlips", "Hr", new { year = slip.Year, month = slip.Month, employeeId = slip.EmployeeId });
        }
        var values = SalaryDayCalculation.EditingValues(slip);
        values["SalaryDays"] = salaryDays.Value;
        return await Edit(new SalarySlipRevisionViewModel { Id = id, Version = version, Values = values,
            DeductionNote = slip.DeductionNote, Reason = "Salary days updated from salary slip list; earnings recalculated." });
    }

    private async Task<SalarySlipRevisionViewModel> Populate(SalarySlipRevisionViewModel model, GeneratedSalarySlip slip)
    {
        model.EmployeeName = await db.Employees.Where(e => e.Id == slip.EmployeeId).Select(e => e.FullName + " (" + e.EmployeeCode + ")").SingleAsync();
        model.Year = slip.Year; model.Month = slip.Month;
        model.History = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "SalarySlipRevision:" + slip.Id).OrderByDescending(a => a.Id).ToListAsync();
        return model;
    }
}
