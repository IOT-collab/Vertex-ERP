using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using System.Text.Json;
namespace VertexERP.Controllers;
[Authorize(Roles = "Admin,HR")]
public sealed class LeaveBalancesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Calculate(int employeeId, int year)
    {
        if (year is < 2000 or > 2100) return BadRequest();
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId);
        if (employee == null) return NotFound();
        var saved = await db.LeaveCalculations.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == year);
        var input = new LeaveCalculationEdit { EmployeeId = employeeId, Year = year,
            Revision = saved?.Revision ?? 0, OpeningEL = saved?.OpeningEL ?? 0, OpeningEW = saved?.OpeningEW ?? 0,
            Months = saved == null ? Enumerable.Range(1, 12).Select(m => new LeaveMonth { Month = m }).ToList() : JsonSerializer.Deserialize<List<LeaveMonth>>(saved.MonthsJson)! };
        await Populate(input);
        input.BalanceRevisions = ManualLeavePage.Categories.Select(c => input.Balances.FirstOrDefault(x => x.Category == c)?.Revision ?? 0).ToList();
        if (saved == null) { input.OpeningEL = Math.Max(0, input.PreviousEL ?? 0); input.OpeningEW = Math.Max(0, input.PreviousEW ?? 0); }
        return View(input);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(LeaveCalculationEdit input)
    {
        if (input.Year is < 2000 or > 2100 || input.Months.Count != 12 || !input.Months.Select(x => x.Month).Order().SequenceEqual(Enumerable.Range(1, 12)) || input.BalanceRevisions.Count != 4) return BadRequest();
        if (!await db.Employees.AnyAsync(x => x.Id == input.EmployeeId)) return NotFound();
        foreach (var month in input.Months)
        {
            if (month.WorkingDays > DateTime.DaysInMonth(input.Year, month.Month)) ModelState.AddModelError("", "Working days cannot exceed calendar days in the month.");
            if (new[] { month.WorkingDays, month.AdjustedEL, month.AdjustedCL, month.AdjustedML, month.EarnedEW, month.AdjustedEW }.Any(x => x % .5m != 0)) ModelState.AddModelError("", "Use whole or half days.");
        }
        if (input.OpeningEL % .5m != 0 || input.OpeningEW % .5m != 0) ModelState.AddModelError("", "Opening balances must use whole or half days.");
        var saved = await db.LeaveCalculations.SingleOrDefaultAsync(x => x.EmployeeId == input.EmployeeId && x.Year == input.Year);
        var balances = await db.ManualLeaveBalances.Where(x => x.EmployeeId == input.EmployeeId && x.Year == input.Year).ToListAsync();
        if (saved == null && balances.Count > 0 && !input.ConfirmReplaceManual)
            ModelState.AddModelError("", "Review the existing manual totals and confirm replacement with this calculation.");
        if (input.Revision != (saved?.Revision ?? 0) || ManualLeavePage.Categories.Where((c, i) => input.BalanceRevisions[i] != (balances.FirstOrDefault(x => x.Category == c)?.Revision ?? 0)).Any())
            ModelState.AddModelError("", "This calculation or balance changed. Reload before saving.");
        if (!ModelState.IsValid) { await Populate(input); return View(input); }
        if (saved == null) { saved = new LeaveCalculation { EmployeeId = input.EmployeeId, Year = input.Year }; db.LeaveCalculations.Add(saved); }
        saved.MonthsJson = JsonSerializer.Serialize(input.Months.OrderBy(x => x.Month));
        saved.OpeningEL = input.OpeningEL; saved.OpeningEW = input.OpeningEW;
        saved.Revision++; saved.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var category in ManualLeavePage.Categories)
        {
            var balance = balances.FirstOrDefault(x => x.Category == category);
            if (balance == null) { balance = new ManualLeaveBalance { EmployeeId = input.EmployeeId, Year = input.Year, Category = category }; db.ManualLeaveBalances.Add(balance); }
            var result = LeaveBalanceCalculator.Calculate(input.Months, category);
            balance.TotalDays = result.Earned + (category == "EL" ? input.OpeningEL : category == "EW" ? input.OpeningEW : 0);
            balance.UsedDays = result.Adjustment;
            balance.Revision++; balance.UpdatedAtUtc = DateTime.UtcNow;
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("The calculation changed or could not be saved. Reload and retry."); }
        TempData["LeaveMessage"] = "Calculation saved. Approved requests and additional HR usage are included in all leave balances.";
        return RedirectToAction(nameof(Index), new { year = input.Year });
    }

    private async Task Populate(LeaveCalculationEdit input)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == input.EmployeeId);
        input.EmployeeName = employee.FullName; input.EmployeeCode = employee.EmployeeCode;
        input.HasSavedCalculation = await db.LeaveCalculations.AsNoTracking().AnyAsync(x => x.EmployeeId == input.EmployeeId && x.Year == input.Year);
        var balances = await db.ManualLeaveBalances.AsNoTracking().Where(x => x.EmployeeId == input.EmployeeId && (x.Year == input.Year || x.Year == input.Year - 1)).ToListAsync();
        var requests = await db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == input.EmployeeId).ToListAsync();
        LeaveBalanceCalculator.ApplyRequests(balances, requests);
        input.Balances = balances.Where(x => x.Year == input.Year).ToList();
        foreach (var category in ManualLeavePage.Categories.Where(c => input.Balances.All(x => x.Category != c))) input.Balances.Add(new ManualLeaveBalance { EmployeeId = input.EmployeeId, Year = input.Year, Category = category });
        LeaveBalanceCalculator.ApplyRequests(input.Balances, requests);
        input.PreviousEL = balances.FirstOrDefault(x => x.Year == input.Year - 1 && x.Category == "EL")?.AvailableDays;
        input.PreviousEW = balances.FirstOrDefault(x => x.Year == input.Year - 1 && x.Category == "EW")?.AvailableDays;
    }
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
        if (await db.LeaveCalculations.AnyAsync(x => x.EmployeeId == input.EmployeeId && x.Year == input.Year)) return BadRequest("Use Calculate Leave to update this employee's saved calculation.");
        if (input.Categories.Count != 4 || !input.Categories.Select(x => x.Category).OrderBy(x => x).SequenceEqual(ManualLeavePage.Categories.OrderBy(x => x)))
            return BadRequest("EL, CL, ML and EW are required.");
        if (!await db.Employees.AnyAsync(x => x.Id == input.EmployeeId)) return NotFound();
        foreach (var category in input.Categories)
        {
            if (category.UsedDays.HasValue != category.AvailableDays.HasValue)
                ModelState.AddModelError("", $"{category.Category}: enter both additional usage and total entitlement, or leave both blank.");
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
            balance.TotalDays = category.AvailableDays!.Value;
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
        var balances = await db.ManualLeaveBalances.AsNoTracking().Where(x => x.Year == year).ToListAsync();
        var first = new DateOnly(year, 1, 1); var last = new DateOnly(year, 12, 31);
        var requests = await db.LeaveRequests.AsNoTracking().Where(x => x.FromDate <= last && x.ToDate >= first).ToListAsync();
        LeaveBalanceCalculator.IncludeUnfilled(balances, requests, year);
        var calculations = await db.LeaveCalculations.AsNoTracking().Where(x => x.Year == year).ToDictionaryAsync(x => x.EmployeeId);
        foreach (var balance in balances)
            balance.OpeningDays = balance.Category == "EL" ? calculations.GetValueOrDefault(balance.EmployeeId)?.OpeningEL ?? 0 : balance.Category == "EW" ? calculations.GetValueOrDefault(balance.EmployeeId)?.OpeningEW ?? 0 : 0;
        return new ManualLeavePage { Year = year, Search = search, Draft = draft,
            Employees = await query.OrderBy(x => x.FullName).ToListAsync(),
            CalculatedEmployeeIds = calculations.Keys.ToList(),
            Balances = balances };
    }
}
