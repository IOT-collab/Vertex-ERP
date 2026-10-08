using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using VertexERP.Data;
    using VertexERP.Models;
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR,Manager")]
    public class ProjectMgmController : Controller
    {
        private readonly ApplicationDbContext _db;
        public ProjectMgmController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> ProjectCreation()
        {
            await Task.CompletedTask;
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "projects" });
        }

        [HttpPost, ValidateAntiForgeryToken, Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> ProjectCreation([Bind("ProjectCode,ProjectName,DepartmentId,ManagerId,StartDate,EndDate,Status,Description")] ErpProject model)
        {
            model.ProjectCode = (model.ProjectCode ?? "").Trim().ToUpperInvariant();
            model.ProjectName = (model.ProjectName ?? "").Trim();
            if (model.ProjectCode.Length == 0) ModelState.AddModelError(nameof(model.ProjectCode), "Project code is required.");
            if (model.ProjectName.Length == 0) ModelState.AddModelError(nameof(model.ProjectName), "Project name is required.");
            if (model.EndDate < model.StartDate) ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after start date.");
            if (await _db.Projects.AnyAsync(x => x.ProjectCode == model.ProjectCode)) ModelState.AddModelError(nameof(model.ProjectCode), "Project code already exists.");
            if (model.DepartmentId.HasValue && !await _db.Departments.AnyAsync(x => x.Id == model.DepartmentId && x.IsActive)) ModelState.AddModelError(nameof(model.DepartmentId), "Select an active department.");
            if (model.ManagerId.HasValue && !await _db.AppUsers.AnyAsync(x => x.EmployeeId == model.ManagerId && x.IsActive && x.Role == "Manager")) ModelState.AddModelError(nameof(model.ManagerId), "Select an active manager.");
            if (ModelState.IsValid)
            {
                _db.Projects.Add(model);
                try { await _db.SaveChangesAsync(); TempData["ProjectMessage"] = "Project saved successfully."; return RedirectToAction(nameof(ProjectCreation)); }
                catch (DbUpdateException) { ModelState.AddModelError("", "Project could not be saved. Check the code and retry."); }
            }
            await LoadProjectsAsync();
            return View("ProjectForm", model);
        }

        private async Task LoadProjectsAsync()
        {
            ViewBag.Departments = new SelectList(await _db.Departments.Where(x => x.IsActive).OrderBy(x => x.DepartmentName).ToListAsync(), "Id", "DepartmentName");
            ViewBag.Managers = new SelectList(await _db.Employees.Where(x => x.IsActive && _db.AppUsers.Any(u => u.EmployeeId == x.Id && u.IsActive && u.Role == "Manager")).OrderBy(x => x.FullName).ToListAsync(), "Id", "FullName");
            ViewBag.Projects = await _db.Projects.AsNoTracking().Include(x => x.Manager).OrderBy(x => x.ProjectCode).ToListAsync();
        }

        public IActionResult AssignTask()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "tasks" });
        }

        public IActionResult ProjectTimeline()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "timeline" });
        }

        public IActionResult ResourceAllocation()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "resources" });
        }

        public IActionResult Timesheet()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "timesheets" });
        }

        public IActionResult BudgetTracking()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "budget" });
        }

        public IActionResult RaisedEnquiry()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "enquiries" });
        }


    }
}
