using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR,Manager")]
[Route("api/task-management")]
[ApiController]
public class TaskManagementController : ControllerBase
{
    private static readonly string[] Priorities = ["Low", "Medium", "High"];
    private readonly ApplicationDbContext _db;

    public TaskManagementController(ApplicationDbContext db) => _db = db;

    [HttpGet("people")]
    public async Task<IActionResult> GetPeople(CancellationToken cancellationToken)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(employee => employee.IsActive)
            .OrderBy(employee => employee.FullName)
            .Select(employee => new
            {
                employee.Id,
                employee.EmployeeCode,
                employee.FullName,
                employee.Email,
                employee.Department,
                employee.DepartmentId,
                employee.Designation,
                employee.ReportingManagerId
            })
            .ToListAsync(cancellationToken);

        var managerIds = (await _db.AppUsers.AsNoTracking()
            .Where(user => user.IsActive && user.Role == "Manager" && user.EmployeeId.HasValue)
            .Select(user => user.EmployeeId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var employeeIds = employees.Select(employee => employee.Id).Where(id => !managerIds.Contains(id)).ToHashSet();
        if (User.IsInRole("Manager"))
        {
            var currentManagerId = await GetCurrentEmployeeIdAsync(cancellationToken);
            var managerDepartmentId = employees.Where(employee => employee.Id == currentManagerId).Select(employee => employee.DepartmentId).FirstOrDefault();
            managerIds.Clear();
            if (currentManagerId.HasValue) managerIds.Add(currentManagerId.Value);
            employeeIds.IntersectWith(managerDepartmentId.HasValue
                ? employees.Where(employee => employee.Id != currentManagerId && employee.DepartmentId == managerDepartmentId).Select(employee => employee.Id)
                : Array.Empty<int>());
        }

        return Ok(new
        {
            managers = employees.Where(employee => managerIds.Contains(employee.Id)),
            employees = employees.Where(employee => employeeIds.Contains(employee.Id))
        });
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentManagerId = User.IsInRole("Manager") ? await GetCurrentEmployeeIdAsync(cancellationToken) : null;
        var tasks = await _db.WorkTasks.AsNoTracking()
            .Where(task => !currentManagerId.HasValue || task.ManagerId == currentManagerId.Value)
            .OrderByDescending(task => task.CreatedAtUtc)
            .Select(task => new
            {
                id = task.Id,
                taskCode = "TSK-" + task.Id.ToString("D5"),
                task.Title,
                task.Description,
                managerId = task.ManagerId,
                managerName = task.Manager.FullName,
                managerCode = task.Manager.EmployeeCode,
                assigneeId = task.AssigneeId,
                assigneeName = task.Assignee.FullName,
                assigneeCode = task.Assignee.EmployeeCode,
                task.Priority,
                task.Status,
                task.DueDate,
                startDate = DateOnly.FromDateTime(task.CreatedAtUtc),
                progress = task.Status == "Completed" ? 100 : task.Status == "In Review" ? 75 : task.Status == "In Progress" ? 40 : 0
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            tasks,
            metrics = new
            {
                managers = await _db.WorkTasks.Select(task => task.ManagerId).Distinct().CountAsync(cancellationToken),
                employees = await _db.WorkTasks.Select(task => task.AssigneeId).Distinct().CountAsync(cancellationToken),
                totalTasks = tasks.Count,
                overdue = tasks.Count(task => task.DueDate < today && task.Status != "Completed")
            }
        });
    }

    [HttpPost("tasks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTask(CreateWorkTaskRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!Priorities.Contains(request.Priority, StringComparer.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(request.Priority), "Priority must be Low, Medium, or High.");
        if (request.DueDate < DateOnly.FromDateTime(DateTime.Today))
            ModelState.AddModelError(nameof(request.DueDate), "Due date cannot be in the past.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var managerId = request.ManagerId!.Value;
        var assigneeId = request.AssigneeId!.Value;
        if (User.IsInRole("Manager"))
        {
            var currentManagerId = await GetCurrentEmployeeIdAsync(cancellationToken);
            var managerDepartmentId = await _db.Employees.Where(employee => employee.Id == currentManagerId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync(cancellationToken);
            if (managerId != currentManagerId || !await _db.Employees.AnyAsync(employee => employee.Id == assigneeId && employee.IsActive && employee.DepartmentId == managerDepartmentId && !_db.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"), cancellationToken))
                return Forbid();
        }
        var people = await _db.Employees
            .Where(employee => employee.IsActive && (employee.Id == managerId || employee.Id == assigneeId))
            .Select(employee => new { employee.Id, employee.ReportingManagerId })
            .ToListAsync(cancellationToken);

        if (people.All(employee => employee.Id != managerId))
            ModelState.AddModelError(nameof(request.ManagerId), "The selected manager is not an active employee.");
        if (people.All(employee => employee.Id != assigneeId))
            ModelState.AddModelError(nameof(request.AssigneeId), "The selected employee is not active.");
        if (managerId == assigneeId)
            ModelState.AddModelError(nameof(request.AssigneeId), "Manager and assignee must be different employees.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var task = new WorkTask
        {
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ManagerId = managerId,
            AssigneeId = assigneeId,
            Priority = Priorities.First(priority => priority.Equals(request.Priority, StringComparison.OrdinalIgnoreCase)),
            DueDate = request.DueDate!.Value
        };
        _db.WorkTasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetTasks), new { id = task.Id }, new { task.Id, message = "Task assigned successfully." });
    }

    [HttpPut("tasks/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTask(int id, UpdateWorkTaskRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (request.Id != id)
            return BadRequest("Task ID mismatch.");
        if (!Priorities.Contains(request.Priority, StringComparer.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(request.Priority), "Priority must be Low, Medium, or High.");
        if (request.DueDate < DateOnly.FromDateTime(DateTime.Today))
            ModelState.AddModelError(nameof(request.DueDate), "Due date cannot be in the past.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task == null)
            return NotFound("Task not found.");

        var managerId = request.ManagerId!.Value;
        var assigneeId = request.AssigneeId!.Value;
        if (User.IsInRole("Manager"))
        {
            var currentManagerId = await GetCurrentEmployeeIdAsync(cancellationToken);
            var managerDepartmentId = await _db.Employees.Where(employee => employee.Id == currentManagerId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync(cancellationToken);
            if (task.ManagerId != currentManagerId || managerId != currentManagerId || !await _db.Employees.AnyAsync(employee => employee.Id == assigneeId && employee.IsActive && employee.DepartmentId == managerDepartmentId && !_db.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"), cancellationToken))
                return Forbid();
        }
        var people = await _db.Employees
            .Where(employee => employee.IsActive && (employee.Id == managerId || employee.Id == assigneeId))
            .Select(employee => new { employee.Id })
            .ToListAsync(cancellationToken);

        if (people.All(employee => employee.Id != managerId))
            ModelState.AddModelError(nameof(request.ManagerId), "The selected manager is not an active employee.");
        if (people.All(employee => employee.Id != assigneeId))
            ModelState.AddModelError(nameof(request.AssigneeId), "The selected employee is not active.");
        if (managerId == assigneeId)
            ModelState.AddModelError(nameof(request.AssigneeId), "Manager and assignee must be different employees.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        task.Title = request.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        task.ManagerId = managerId;
        task.AssigneeId = assigneeId;
        task.Priority = Priorities.First(priority => priority.Equals(request.Priority, StringComparison.OrdinalIgnoreCase));
        task.DueDate = request.DueDate!.Value;
        task.UpdatedAtUtc = DateTime.UtcNow;

        _db.WorkTasks.Update(task);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Task updated successfully." });
    }

    [HttpDelete("tasks/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTask(int id, CancellationToken cancellationToken)
    {
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task == null)
            return NotFound("Task not found.");
        if (User.IsInRole("Manager") && task.ManagerId != await GetCurrentEmployeeIdAsync(cancellationToken))
            return Forbid();

        _db.WorkTasks.Remove(task);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Task deleted successfully." });
    }

    private async Task<int?> GetCurrentEmployeeIdAsync(CancellationToken cancellationToken)
    {
        var userIdText = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdText, out var userId)
            ? await _db.AppUsers.AsNoTracking().Where(user => user.Id == userId).Select(user => user.EmployeeId).FirstOrDefaultAsync(cancellationToken)
            : null;
    }
}
