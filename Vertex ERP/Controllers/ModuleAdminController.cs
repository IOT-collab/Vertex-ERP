using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Services;
namespace VertexERP.Controllers;
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ModuleAdminController(ApplicationDbContext db) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id, bool isActive, long revision)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid module change.");
        var module = ModuleCatalog.All.SingleOrDefault(x => x.Id == id);
        if (module == null) return BadRequest("Unknown module.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Serialize module transitions across all app instances, including dependency checks.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(782419065)");
            var states = await db.ModuleStates.ToDictionaryAsync(x => x.Id);
            var state = states[id];
            if (state.Revision != revision)
            {
                TempData["ModuleMessage"] = "Another administrator changed this module. Review its current status and try again.";
                return RedirectToAction("AdminPanel", "Main");
            }
            if ((!isActive && (id is "hr" or "attendance" or "leave") && states["payroll"].IsActive)
                || (isActive && id == "payroll" && (!states["hr"].IsActive || !states["attendance"].IsActive || !states["leave"].IsActive)))
            {
                TempData["ModuleMessage"] = "Payroll requires HR, Attendance and Leave. Deactivate Payroll first, or activate all three before Payroll.";
                return RedirectToAction("AdminPanel", "Main");
            }
            if (state.IsActive != isActive)
            {
                state.IsActive = isActive;
                state.Revision++;
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            TempData["ModuleMessage"] = $"{module.Name} {(isActive ? "activated" : "deactivated")}. Existing records are preserved.";
            return RedirectToAction("AdminPanel", "Main");
        });
    }
}
