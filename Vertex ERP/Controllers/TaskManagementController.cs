using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
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
                employee.Designation,
                employee.ReportingManagerId
            })
            .ToListAsync(cancellationToken);

        var departmentManagerIds = await _db.Departments.AsNoTracking()
            .Where(department => department.ManagerId != null)
            .Select(department => department.ManagerId!.Value)
            .ToListAsync(cancellationToken);
        var reportingManagerIds = employees.Where(employee => employee.ReportingManagerId != null)
            .Select(employee => employee.ReportingManagerId!.Value);
        var managerIds = departmentManagerIds.Concat(reportingManagerIds).ToHashSet();

        return Ok(new
        {
            managers = employees.Where(employee => managerIds.Contains(employee.Id)),
            employees
        });
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = await _db.WorkTasks.AsNoTracking()
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
}
