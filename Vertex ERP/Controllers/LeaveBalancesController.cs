using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
namespace VertexERP.Controllers;
[Authorize(Roles = "Admin,HR")]
public sealed class LeaveBalancesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? year, string? search)
    {
        var selectedYear = year ?? DateTime.Today.Year;
        if (selectedYear is < 2000 or > 2100) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        return View(await Page(selectedYear, search));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ManualLeaveEmployeeEdit input)
    {
        if (input.Categories.Count != 4 || !input.Categories.Select(x => x.Category).OrderBy(x => x).SequenceEqual(ManualLeavePage.Categories.OrderBy(x => x)))
            return BadRequest("EL, CL, ML and EW are required.");
        if (!await db.Employees.AnyAsync(x => x.Id == input.EmployeeId)) return NotFound();
        foreach (var category in input.Categories)
        {
            if (category.UsedDays.HasValue != category.AvailableDays.HasValue)
                ModelState.AddModelError("", $"{category.Category}: enter both Used and Available, or leave both blank.");
            if (category.UsedDays % 0.5m is decimal used && used != 0 || category.AvailableDays % 0.5m is decimal available && available != 0)
                ModelState.AddModelError("", $"{category.Category}: use whole or half days.");
        }
        if (!ModelState.IsValid) return View("Index", await Page(Math.Clamp(input.Year, 2000, 2100), null, input));
        var balances = await db.ManualLeaveBalances.Where(x => x.EmployeeId == input.EmployeeId && x.Year == input.Year).ToDictionaryAsync(x => x.Category);
        if (input.Categories.Any(x => x.Revision != (balances.GetValueOrDefault(x.Category)?.Revision ?? 0)))
        {
            ModelState.AddModelError("", "This employee's balance changed. Reload before saving.");
            return View("Index", await Page(input.Year, null, input));
        }
        foreach (var category in input.Categories)
        {
            balances.TryGetValue(category.Category, out var balance);
            if (!category.UsedDays.HasValue)
            {
                if (balance != null) db.ManualLeaveBalances.Remove(balance);
                continue;
            }
            if (balance == null)
            {
                balance = new ManualLeaveBalance { EmployeeId = input.EmployeeId, Year = input.Year, Category = category.Category };
                db.ManualLeaveBalances.Add(balance);
            }
            balance.UsedDays = category.UsedDays.Value;
            balance.TotalDays = category.UsedDays.Value + category.AvailableDays!.Value;
            balance.Revision++; balance.UpdatedAtUtc = DateTime.UtcNow;
        }
        try
        {
            await db.SaveChangesAsync();
            TempData["LeaveMessage"] = "Saved. This employee can now see the same Used and Available leaves in their login.";
            return RedirectToAction(nameof(Index), new { year = input.Year });
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "The balance changed or could not be saved. Reload and retry.");
            return View("Index", await Page(input.Year, null, input));
        }
    }
    private async Task<ManualLeavePage> Page(int year, string? search, ManualLeaveEmployeeEdit? draft = null)
    {
        var query = db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.EmployeeCode.ToLower().Contains(term));
        }
        return new ManualLeavePage { Year = year, Search = search, Draft = draft,
            Employees = await query.OrderBy(x => x.FullName).ToListAsync(),
            Balances = await db.ManualLeaveBalances.AsNoTracking().Where(x => x.Year == year).ToListAsync() };
    }
}
