using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR,Manager")]
public sealed class ProjectWorkspaceController(ApplicationDbContext db) : Controller
{
    private bool CanManage => User.IsInRole("Admin") || User.IsInRole("HR");
    private async Task<int?> EmployeeIdAsync() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? await db.AppUsers.Where(u => u.Id == id && u.IsActive).Select(u => u.EmployeeId).FirstOrDefaultAsync() : null;
    private async Task<IQueryable<ErpProject>> ScopeAsync()
    {
        var query = db.Projects.AsQueryable();
        if (CanManage) return query;
        var employeeId = await EmployeeIdAsync();
        return query.Where(p => employeeId.HasValue && p.ManagerId == employeeId);
    }
    private async Task<ErpProject?> ProjectAsync(int id) => await (await ScopeAsync()).FirstOrDefaultAsync(p => p.Id == id);
    private async Task<ProjectWorkspace> LoadAsync(string section, int? projectId = null)
    {
        var projects = await (await ScopeAsync()).AsNoTracking().Include(p => p.Manager).Include(p => p.Team).OrderBy(p => p.ProjectName).ToListAsync();
        var ids = projects.Select(p => p.Id).ToArray();
        var teamIds = projects.Where(p => p.TeamId.HasValue).Select(p => p.TeamId!.Value).ToArray();
        var assignments = await db.ProjectEmployees.AsNoTracking().Include(a => a.Employee).Where(a => ids.Contains(a.ProjectId)).ToListAsync();
        var employeeIds = assignments.Select(a => a.EmployeeId).Distinct().ToArray();
        return new ProjectWorkspace
        {
            Section = section, CanManageProjects = CanManage, SelectedProjectId = projectId,
            Projects = projects, Assignments = assignments,
            Departments = CanManage ? await db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.DepartmentName).ToListAsync() : [],
            Managers = CanManage ? await db.Employees.AsNoTracking().Where(e => e.IsActive && db.AppUsers.Any(u => u.EmployeeId == e.Id && u.IsActive && u.Role == "Manager")).OrderBy(e => e.FullName).ToListAsync() : [],
            Employees = await db.Employees.AsNoTracking().Where(e => e.IsActive && (CanManage || employeeIds.Contains(e.Id))).OrderBy(e => e.FullName).ToListAsync(),
            Teams = await db.ProjectTeams.AsNoTracking().Where(t => CanManage || teamIds.Contains(t.Id)).OrderBy(t => t.Name).ToListAsync(),
            TeamMembers = await db.ProjectTeamMembers.AsNoTracking().Include(m => m.Employee).Where(m => CanManage || teamIds.Contains(m.TeamId)).ToListAsync(),
            Tasks = await db.WorkTasks.AsNoTracking().Include(t => t.Assignee).Where(t => t.ProjectId.HasValue && ids.Contains(t.ProjectId.Value)).OrderBy(t => t.DueDate).ToListAsync(),
            Timesheets = await db.ProjectTimeEntries.AsNoTracking().Include(t => t.Employee).Where(t => ids.Contains(t.ProjectId)).OrderByDescending(t => t.WorkDate).ToListAsync(),
            Costs = await db.ProjectCosts.AsNoTracking().Where(c => ids.Contains(c.ProjectId)).OrderByDescending(c => c.ExpenseDate).ToListAsync(),
            Enquiries = await db.ProjectEnquiries.AsNoTracking().Where(e => ids.Contains(e.ProjectId)).OrderByDescending(e => e.CreatedAtUtc).ToListAsync()
        };
    }
    [HttpGet]
    public async Task<IActionResult> Index(string section = "projects", int? projectId = null, int? editId = null)
    {
        if (!new[] { "projects", "teams", "tasks", "timeline", "resources", "timesheets", "budget", "enquiries" }.Contains(section)) return NotFound();
        if (projectId.HasValue && await ProjectAsync(projectId.Value) == null) return NotFound();
        var model = await LoadAsync(section, projectId);
        if (editId.HasValue)
        {
            switch (section)
            {
                case "projects":
                    if (!CanManage) return Forbid();
                    var project = model.Projects.FirstOrDefault(p => p.Id == editId); if (project == null) return NotFound();
                    model.ProjectForm = project; break;
                case "teams":
                    if (!CanManage) return Forbid();
                    var team = model.Teams.FirstOrDefault(t => t.Id == editId); if (team == null) return NotFound();
                    model.TeamForm = team; model.MemberIds = model.TeamMembers.Where(m => m.TeamId == team.Id).Select(m => m.EmployeeId).ToArray(); break;
                case "tasks":
                    var task = model.Tasks.FirstOrDefault(t => t.Id == editId); if (task == null) return NotFound();
                    model.TaskForm = new() { Id = task.Id, ProjectId = task.ProjectId!.Value, Title = task.Title, Description = task.Description, AssigneeId = task.AssigneeId, DueDate = task.DueDate, Priority = task.Priority, Status = task.Status }; break;
                case "timesheets":
                    var time = model.Timesheets.FirstOrDefault(t => t.Id == editId); if (time == null) return NotFound(); model.TimeForm = time; break;
                case "budget":
                    if (!CanManage) return Forbid();
                    var cost = model.Costs.FirstOrDefault(c => c.Id == editId); if (cost == null) return NotFound(); model.CostForm = cost; break;
                case "enquiries":
                    var enquiry = model.Enquiries.FirstOrDefault(e => e.Id == editId); if (enquiry == null) return NotFound(); model.EnquiryForm = enquiry; break;
                default: return BadRequest();
            }
        }
        return View(model);
    }
    private IActionResult Done(string section, string message, int? projectId = null)
    {
        TempData["ProjectMessage"] = message;
        return RedirectToAction(nameof(Index), new { section, projectId });
    }
    private async Task<bool> SaveAsync()
    {
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException) { ModelState.AddModelError("", "Unable to save. A record may have changed or the name/code is already in use. Refresh and try again."); return false; }
    }
    private async Task<bool> MemberAsync(int projectId, int employeeId) => await db.ProjectEmployees.AnyAsync(a => a.ProjectId == projectId && a.EmployeeId == employeeId && a.Employee.IsActive);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveProject([Bind(Prefix = "ProjectForm")] ErpProject input)
    {
        input.ProjectCode = (input.ProjectCode ?? "").Trim().ToUpperInvariant(); input.ProjectName = (input.ProjectName ?? "").Trim();
        if (input.ProjectCode.Length == 0 || input.ProjectName.Length == 0) ModelState.AddModelError("", "Project name and code are required.");
        if (input.EndDate < input.StartDate) ModelState.AddModelError("", "End date must be on or after start date.");
        if (decimal.Round(input.Budget, 2) != input.Budget) ModelState.AddModelError("", "Budget can have at most two decimal places.");
        if (input.Id != 0 && (await db.WorkTasks.AnyAsync(t => t.ProjectId == input.Id && (t.DueDate < input.StartDate || t.DueDate > input.EndDate)) ||
            await db.ProjectTimeEntries.AnyAsync(t => t.ProjectId == input.Id && (t.WorkDate < input.StartDate || t.WorkDate > input.EndDate))))
            ModelState.AddModelError("", "Project dates must include existing task due dates and recorded work dates.");
        if (await db.Projects.AnyAsync(p => p.Id != input.Id && p.ProjectCode.ToLower() == input.ProjectCode.ToLower())) ModelState.AddModelError("", "Project code already exists.");
        if (input.DepartmentId.HasValue && !await db.Departments.AnyAsync(d => d.Id == input.DepartmentId && d.IsActive)) ModelState.AddModelError("", "Select an active department.");
        if (!input.ManagerId.HasValue || !await db.Employees.AnyAsync(e => e.Id == input.ManagerId && e.IsActive && db.AppUsers.Any(u => u.EmployeeId == e.Id && u.IsActive && u.Role == "Manager"))) ModelState.AddModelError("", "Select an active manager.");
        if (!input.TeamId.HasValue || !await db.ProjectTeams.AnyAsync(t => t.Id == input.TeamId && t.IsActive)) ModelState.AddModelError("", "Select an active team. Create a team in Teams first.");
        var project = input.Id == 0 ? new ErpProject() : await db.Projects.FindAsync(input.Id);
        if (project == null) return NotFound();
        if (ModelState.IsValid)
        {
            var teamChanged = project.TeamId != input.TeamId || project.Id == 0;
            project.ProjectCode = input.ProjectCode; project.ProjectName = input.ProjectName; project.DepartmentId = input.DepartmentId;
            project.ManagerId = input.ManagerId; project.TeamId = input.TeamId; project.StartDate = input.StartDate; project.EndDate = input.EndDate;
            project.Status = input.Status; project.Description = input.Description; project.Budget = input.Budget;
            if (project.Id == 0) db.Projects.Add(project);
            if (teamChanged)
            {
                var existing = project.Id == 0 ? new List<ProjectEmployee>() : await db.ProjectEmployees.Where(a => a.ProjectId == project.Id).ToListAsync();
                var memberIds = await db.ProjectTeamMembers.Where(m => m.TeamId == input.TeamId && m.Employee.IsActive).Select(m => m.EmployeeId).ToListAsync();
                // Keep individual allocations and history; changing teams adds the new team's members.
                foreach (var id in memberIds.Except(existing.Select(a => a.EmployeeId))) db.ProjectEmployees.Add(new ProjectEmployee { Project = project, EmployeeId = id });
            }
            if (project.Id != 0)
                foreach (var task in await db.WorkTasks.Where(t => t.ProjectId == project.Id).ToListAsync()) task.ManagerId = input.ManagerId!.Value;
            if (await SaveAsync()) return Done("projects", "Project saved. Manager and team assignments are ready.");
        }
        var model = await LoadAsync("projects"); model.ProjectForm = input; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveTeam([Bind(Prefix = "TeamForm")] ProjectTeam input, int[] memberIds)
    {
        input.Name = (input.Name ?? "").Trim(); memberIds = memberIds.Distinct().ToArray();
        if (input.Name.Length == 0) ModelState.AddModelError("", "Team name is required.");
        if (await db.ProjectTeams.AnyAsync(t => t.Id != input.Id && t.Name.ToLower() == input.Name.ToLower())) ModelState.AddModelError("", "Team name already exists.");
        if (memberIds.Length == 0 || await db.Employees.CountAsync(e => memberIds.Contains(e.Id) && e.IsActive) != memberIds.Length) ModelState.AddModelError("", "Select at least one active employee.");
        var team = input.Id == 0 ? new ProjectTeam() : await db.ProjectTeams.FindAsync(input.Id); if (team == null) return NotFound();
        if (ModelState.IsValid)
        {
            team.Name = input.Name; team.Description = input.Description; team.IsActive = input.IsActive;
            if (team.Id == 0) db.ProjectTeams.Add(team);
            var existing = team.Id == 0 ? new List<ProjectTeamMember>() : await db.ProjectTeamMembers.Where(m => m.TeamId == team.Id).ToListAsync();
            db.ProjectTeamMembers.RemoveRange(existing.Where(m => !memberIds.Contains(m.EmployeeId)));
            foreach (var id in memberIds.Except(existing.Select(m => m.EmployeeId))) db.ProjectTeamMembers.Add(new ProjectTeamMember { Team = team, EmployeeId = id });
            if (await SaveAsync()) return Done("teams", "Team saved. Existing project allocations can be changed in Resource Allocation.");
        }
        var model = await LoadAsync("teams"); model.TeamForm = input; model.MemberIds = memberIds; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTask([Bind(Prefix = "TaskForm")] ProjectTaskInput input)
    {
        var project = await ProjectAsync(input.ProjectId); if (project == null) return NotFound();
        var task = input.Id == 0 ? new WorkTask() : await db.WorkTasks.FindAsync(input.Id);
        if (task == null || (task.Id != 0 && task.ProjectId != input.ProjectId)) return NotFound();
        if (!project.ManagerId.HasValue) ModelState.AddModelError("", "Assign a project manager first.");
        if (!await MemberAsync(input.ProjectId, input.AssigneeId)) ModelState.AddModelError("", "Select an active employee allocated to this project.");
        if (input.AssigneeId == project.ManagerId) ModelState.AddModelError("", "Select a team employee other than the project manager.");
        if (input.DueDate < project.StartDate || input.DueDate > project.EndDate) ModelState.AddModelError("", "Task due date must be within the project dates.");
        if (string.IsNullOrWhiteSpace(input.Title)) ModelState.AddModelError("", "Task title is required.");
        if (ModelState.IsValid)
        {
            task.ProjectId = project.Id; task.ManagerId = project.ManagerId!.Value; task.AssigneeId = input.AssigneeId;
            task.Title = input.Title.Trim(); task.Description = input.Description; task.Priority = input.Priority; task.Status = input.Status;
            task.DueDate = input.DueDate; task.UpdatedAtUtc = DateTime.UtcNow; if (task.Id == 0) db.WorkTasks.Add(task);
            if (await SaveAsync()) return Done("tasks", "Task saved.", project.Id);
        }
        var model = await LoadAsync("tasks", project.Id); model.TaskForm = input; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> Allocate(int projectId, int[] memberIds)
    {
        if (await ProjectAsync(projectId) == null) return NotFound();
        memberIds = memberIds.Distinct().ToArray();
        if (await db.Employees.CountAsync(e => memberIds.Contains(e.Id) && e.IsActive) != memberIds.Length) ModelState.AddModelError("", "Select active employees only.");
        var existing = await db.ProjectEmployees.Where(a => a.ProjectId == projectId).ToListAsync();
        var removed = existing.Where(a => !memberIds.Contains(a.EmployeeId)).Select(a => a.EmployeeId).ToArray();
        if (await db.WorkTasks.AnyAsync(t => t.ProjectId == projectId && removed.Contains(t.AssigneeId) && t.Status != "Completed")) ModelState.AddModelError("", "Reassign or complete open tasks before removing their employees.");
        if (ModelState.IsValid)
        {
            db.ProjectEmployees.RemoveRange(existing.Where(a => removed.Contains(a.EmployeeId)));
            foreach (var id in memberIds.Except(existing.Select(a => a.EmployeeId))) db.ProjectEmployees.Add(new ProjectEmployee { ProjectId = projectId, EmployeeId = id });
            if (await SaveAsync()) return Done("resources", "Project employee allocation saved.", projectId);
        }
        var model = await LoadAsync("resources", projectId); model.MemberIds = memberIds; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTime([Bind(Prefix = "TimeForm")] ProjectTimeEntry input)
    {
        var project = await ProjectAsync(input.ProjectId); if (project == null) return NotFound();
        var entry = input.Id == 0 ? new ProjectTimeEntry() : await db.ProjectTimeEntries.FindAsync(input.Id);
        if (entry == null || (entry.Id != 0 && entry.ProjectId != input.ProjectId)) return NotFound();
        if (!await MemberAsync(project.Id, input.EmployeeId)) ModelState.AddModelError("", "Select an active employee allocated to this project.");
        if (decimal.Round(input.Hours, 2) != input.Hours) ModelState.AddModelError("", "Hours can have at most two decimal places.");
        if (input.WorkDate > DateOnly.FromDateTime(DateTime.Today) || input.WorkDate < project.StartDate || input.WorkDate > project.EndDate) ModelState.AddModelError("", "Work date must be within the project dates and cannot be in the future.");
        if (ModelState.IsValid)
        {
            // Serialize day totals across concurrent submissions and across projects.
            var saved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"ProjectTimeEntries\" IN SHARE ROW EXCLUSIVE MODE");
                var hours = await db.ProjectTimeEntries.Where(t => t.Id != input.Id && t.EmployeeId == input.EmployeeId && t.WorkDate == input.WorkDate).SumAsync(t => t.Hours);
                if (hours + input.Hours > 24) { ModelState.AddModelError("", "Total employee hours across all projects cannot exceed 24 per day."); return false; }
                entry.ProjectId = input.ProjectId; entry.EmployeeId = input.EmployeeId; entry.WorkDate = input.WorkDate; entry.Hours = input.Hours; entry.Notes = input.Notes;
                if (entry.Id == 0) db.ProjectTimeEntries.Add(entry);
                if (!await SaveAsync()) return false;
                await transaction.CommitAsync(); return true;
            });
            if (saved) return Done("timesheets", "Work hours saved.", project.Id);
        }
        var model = await LoadAsync("timesheets", project.Id); model.TimeForm = input; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveCost([Bind(Prefix = "CostForm")] ProjectCost input)
    {
        var project = await ProjectAsync(input.ProjectId); if (project == null) return NotFound();
        var cost = input.Id == 0 ? new ProjectCost() : await db.ProjectCosts.FindAsync(input.Id);
        if (cost == null || (cost.Id != 0 && cost.ProjectId != input.ProjectId)) return NotFound();
        if (input.ExpenseDate > DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError("", "Expense date cannot be in the future.");
        if (decimal.Round(input.Amount, 2) != input.Amount) ModelState.AddModelError("", "Amount can have at most two decimal places.");
        if (ModelState.IsValid)
        {
            cost.ProjectId = input.ProjectId; cost.Title = input.Title; cost.Amount = input.Amount; cost.ExpenseDate = input.ExpenseDate; cost.Notes = input.Notes;
            if (cost.Id == 0) db.ProjectCosts.Add(cost);
            if (await SaveAsync()) return Done("budget", "Project cost saved.", project.Id);
        }
        var model = await LoadAsync("budget", project.Id); model.CostForm = input; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEnquiry([Bind(Prefix = "EnquiryForm")] ProjectEnquiry input)
    {
        if (await ProjectAsync(input.ProjectId) == null) return NotFound();
        var entry = input.Id == 0 ? new ProjectEnquiry() : await db.ProjectEnquiries.FindAsync(input.Id);
        if (entry == null || (entry.Id != 0 && entry.ProjectId != input.ProjectId)) return NotFound();
        if ((input.Status is "Resolved" or "Closed") && string.IsNullOrWhiteSpace(input.Resolution)) ModelState.AddModelError("", "Enter a resolution before resolving or closing an enquiry.");
        if (ModelState.IsValid)
        {
            entry.ProjectId = input.ProjectId; entry.Subject = input.Subject; entry.Description = input.Description; entry.Status = input.Status; entry.Resolution = input.Resolution;
            if (entry.Id == 0) db.ProjectEnquiries.Add(entry);
            if (await SaveAsync()) return Done("enquiries", "Enquiry saved.", entry.ProjectId);
        }
        var model = await LoadAsync("enquiries", input.ProjectId); model.EnquiryForm = input; return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEntry(string section, int id)
    {
        int projectId;
        switch (section)
        {
            case "tasks":
                var task = await db.WorkTasks.FindAsync(id); if (task?.ProjectId == null || await ProjectAsync(task.ProjectId.Value) == null) return NotFound();
                projectId = task.ProjectId.Value; db.WorkTasks.Remove(task); break;
            case "timesheets":
                var time = await db.ProjectTimeEntries.FindAsync(id); if (time == null || await ProjectAsync(time.ProjectId) == null) return NotFound();
                projectId = time.ProjectId; db.ProjectTimeEntries.Remove(time); break;
            case "budget":
                if (!CanManage) return Forbid();
                var cost = await db.ProjectCosts.FindAsync(id); if (cost == null) return NotFound(); projectId = cost.ProjectId; db.ProjectCosts.Remove(cost); break;
            case "enquiries":
                var enquiry = await db.ProjectEnquiries.FindAsync(id); if (enquiry == null || await ProjectAsync(enquiry.ProjectId) == null) return NotFound();
                projectId = enquiry.ProjectId; db.ProjectEnquiries.Remove(enquiry); break;
            default: return BadRequest();
        }
        if (await SaveAsync()) return Done(section, "Record deleted.", projectId);
        return View("Index", await LoadAsync(section, projectId));
    }
}
