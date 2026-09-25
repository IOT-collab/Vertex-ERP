using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public sealed class SalarySlipRevisionController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (slip == null) return NotFound();
        return View(await Populate(new SalarySlipRevisionViewModel { Id = id, Version = slip.UpdatedAtUtc.Ticks,
            Values = SalarySlipRevisionViewModel.Snapshot(slip), DeductionNote = slip.DeductionNote }, slip));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SalarySlipRevisionViewModel model)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleOrDefaultAsync(s => s.Id == model.Id);
        if (slip == null) return NotFound();
        foreach (var field in SalarySlipRevisionViewModel.Fields)
        {
            var max = field == "SalaryDays" ? DateTime.DaysInMonth(slip.Year, slip.Month) : 100000000m;
            if (!model.Values.TryGetValue(field, out var value) || value < 0 || value > max || decimal.Round(value, 2) != value)
                ModelState.AddModelError("Values[" + field + "]", $"Enter {field} between 0 and {max}, with up to two decimal places.");
        }
        if (string.IsNullOrWhiteSpace(model.Reason)) ModelState.AddModelError(nameof(model.Reason), "A correction reason is required.");
        if (ModelState.IsValid)
        {
            var v = model.Values;
            if (v["ProvidentFund"] + v["ProfessionalTax"] + v["Tds"] + v["OtherDeductions"] + v["LeaveDeduction"] >
                v["BasicSalary"] + v["HouseRentAllowance"] + v["ConveyanceAllowance"] + v["SpecialAllowance"])
                ModelState.AddModelError("", "Total deductions cannot exceed gross salary.");
        }
        if (!ModelState.IsValid) return View(await Populate(model, slip));
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
                typeof(GeneratedSalarySlip).GetProperty(field)!.SetValue(current, model.Values[field]);
            current.DeductionNote = model.DeductionNote?.Trim();
            current.UpdatedAtUtc = DateTime.UtcNow;
            db.AuditLogs.Add(new AuditLog { EntityType = "SalarySlipRevision:" + current.Id, Action = "Salary slip revised",
                ActorId = actor!, ActorName = User.Identity?.Name ?? actor!, ActorRole = User.FindFirstValue(ClaimTypes.Role) ?? "",
                Detail = JsonSerializer.Serialize(new { Reason = model.Reason.Trim(), Before = before, After = model.Values,
                    PreviousNote = previousNote, Note = current.DeductionNote }) });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        });
        if (!saved)
        {
            ModelState.AddModelError("", "This slip was revised by another request. Reload the page before making your correction.");
            return View(await Populate(model, slip));
        }
        TempData["SalaryMessage"] = "Salary slip corrected. The employee can download the updated PDF.";
        return RedirectToAction("SalarySlips", "Hr", new { year = slip.Year, month = slip.Month, employeeId = slip.EmployeeId });
    }

    private async Task<SalarySlipRevisionViewModel> Populate(SalarySlipRevisionViewModel model, GeneratedSalarySlip slip)
    {
        model.EmployeeName = await db.Employees.Where(e => e.Id == slip.EmployeeId).Select(e => e.FullName + " (" + e.EmployeeCode + ")").SingleAsync();
        model.Year = slip.Year; model.Month = slip.Month;
        model.History = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "SalarySlipRevision:" + slip.Id).OrderByDescending(a => a.Id).ToListAsync();
        return model;
    }
}
