using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public sealed class TeamOverviewController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? managerId, int? projectId, string? department, string tab = "managers")
    {
        var employees = await db.Employees.AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
        var projects = await db.Projects.AsNoTracking().Include(x => x.Manager).OrderBy(x => x.ProjectName).ToListAsync();
        var managerIds = (await db.AppUsers.AsNoTracking().Where(x => x.Role == "Manager" && x.EmployeeId.HasValue)
            .Select(x => x.EmployeeId!.Value).ToListAsync())
            .Concat(employees.Where(x => x.ReportingManagerId.HasValue).Select(x => x.ReportingManagerId!.Value))
            .Concat(projects.Where(x => x.ManagerId.HasValue).Select(x => x.ManagerId!.Value)).ToHashSet();
        return View(new TeamProjectOverview
        {
            Employees = employees, Managers = employees.Where(x => managerIds.Contains(x.Id)).ToList(), Projects = projects,
            Assignments = await db.ProjectEmployees.AsNoTracking().ToListAsync(),
            ManagerId = managerId, ProjectId = projectId, Department = department, Tab = tab == "projects" ? "projects" : "managers"
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int projectId, int[] employeeIds)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId)) return NotFound();
        var ids = employeeIds.Distinct().ToArray();
        if (ids.Length == 0 || await db.Employees.CountAsync(x => ids.Contains(x.Id) && x.IsActive) != ids.Length)
        {
            TempData["TeamMessage"] = "Select at least one active employee.";
            return RedirectToAction(nameof(Index), new { tab = "projects", projectId });
        }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var id in ids)
            {
                var added = await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ProjectEmployees\" (\"ProjectId\", \"EmployeeId\") VALUES ({projectId}, {id}) ON CONFLICT DO NOTHING");
                if (added > 0)
                    db.AuditLogs.Add(VertexERP.Services.AuditLogFactory.CreateEvent("ProjectEmployee", "Project employee assigned", $"Project #{projectId} · Employee #{id}", User));
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        TempData["TeamMessage"] = "Employees assigned to the project.";
        return RedirectToAction(nameof(Index), new { tab = "projects", projectId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int projectId, int employeeId)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var removed = await db.ProjectEmployees.Where(x => x.ProjectId == projectId && x.EmployeeId == employeeId).ExecuteDeleteAsync();
            if (removed > 0)
                db.AuditLogs.Add(VertexERP.Services.AuditLogFactory.CreateEvent("ProjectEmployee", "Project employee removed", $"Project #{projectId} · Employee #{employeeId}", User));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        TempData["TeamMessage"] = "Employee removed from the project.";
        return RedirectToAction(nameof(Index), new { tab = "projects", projectId });
    }
}
