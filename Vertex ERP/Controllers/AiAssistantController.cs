using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize]
public sealed class AiAssistantController(ApplicationDbContext db) : Controller
{
    private const int MaximumMessageLength = 500;
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminBriefing(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(FieldAttendanceClock.Now.Date);
        var pendingLeaves = await db.LeaveRequests.AsNoTracking()
            .Where(leave => leave.Status == "Pending")
            .OrderBy(leave => leave.FromDate)
            .ThenBy(leave => leave.AppliedAtUtc)
            .Select(leave => new
            {
                leave.Id,
                employee = leave.Employee.FullName,
                leave.Employee.EmployeeCode,
                leave.LeaveType,
                from = leave.FromDate.ToString("dd MMM yyyy"),
                to = leave.ToDate.ToString("dd MMM yyyy"),
                leave.ApprovalLevel,
                approver = leave.AssignedApproverEmployee != null ? leave.AssignedApproverEmployee.FullName : "HR / Admin",
                submittedAt = leave.AppliedAtUtc
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var priorityTasks = await db.WorkTasks.AsNoTracking()
            .Where(task => task.Status != "Completed" && (task.Priority == "High" || task.DueDate < today))
            .OrderBy(task => task.DueDate < today ? 0 : 1)
            .ThenBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority == "High")
            .Select(task => new
            {
                task.Id,
                task.Title,
                assignee = task.Assignee.FullName,
                task.Priority,
                task.Status,
                due = task.DueDate.ToString("dd MMM yyyy"),
                overdue = task.DueDate < today
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        return Json(new
        {
            asOf = today.ToString("dd MMM yyyy"),
            pendingLeaveCount = await db.LeaveRequests.AsNoTracking().CountAsync(leave => leave.Status == "Pending", cancellationToken),
            priorityTaskCount = await db.WorkTasks.AsNoTracking().CountAsync(task => task.Status != "Completed" && (task.Priority == "High" || task.DueDate < today), cancellationToken),
            pendingLeaves,
            priorityTasks
        });
    }

    [HttpGet]
    [Authorize(Roles = "Admin,HR,Manager")]
    public IActionResult Index() => User.IsInRole("Manager")
        ? RedirectToAction("Manager", "Main")
        : RedirectToAction("Dashboard", "Main");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,HR,Manager")]
    public async Task<IActionResult> Context([FromBody] AssistantRequest request, CancellationToken cancellationToken)
    {
        var question = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaximumMessageLength)
            return BadRequest(new { error = $"Enter a question of {MaximumMessageLength} characters or fewer." });

        var asksAttendance = HasAny(question, "attendance", "present", "absent", "late", "punch", "check in", "check-in", "checkin", "aaj attendance", "aaj present", "today's attendance", "today attendance", "on time", "office")
            && !HasAny(question, "attendance policy", "what is attendance", "how attendance works", "mark attendance", "attendance kaise", "office policy");
        var asksLeave = HasAny(question, "leave", "chhutti", "chutti", "छुट्टी", "on leave")
            && HasAny(question, "pending", "approved", "rejected", "on leave", "who", "list", "show", "count", "how many", "team", "employee", "today", "current", "status", "kaun", "kitne", "dikhao", "request");
        var asksTasks = HasAny(question, "task", "workload", "overdue", "pending work", "assigned work", "kaam", "काम")
            && HasAny(question, "pending", "open", "overdue", "complete", "status", "who", "list", "show", "count", "how many", "team", "employee", "assigned", "kitne", "kaun", "dikhao", "today", "current", "what are");
        var asksPeople = HasAny(question, "employee", "employees", "staff", "team member", "team members", "headcount", "workforce")
            && HasAny(question, "who", "list", "show", "name", "count", "how many", "kitne", "kaun", "dikhao");

        if (!asksAttendance && !asksLeave && !asksTasks && !asksPeople)
            return Json(new { context = (string?)null });

        var employeesQuery = db.Employees.AsNoTracking().Where(employee => employee.IsActive);
        int? managerEmployeeId = null;
        if (User.IsInRole("Manager"))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userId, out var parsedUserId)) return Forbid();

            managerEmployeeId = await db.AppUsers.AsNoTracking()
                .Where(user => user.Id == parsedUserId && user.IsActive && (user.Role == "Manager" || user.Role == "Supervisor"))
                .Select(user => user.EmployeeId)
                .SingleOrDefaultAsync(cancellationToken);
            if (!managerEmployeeId.HasValue) return Forbid();

            employeesQuery = employeesQuery.Where(employee => employee.ReportingManagerId == managerEmployeeId.Value);
        }

        var employees = await employeesQuery
            .OrderBy(employee => employee.FullName)
            .Select(employee => new AssistantEmployee(employee.Id, employee.FullName, employee.EmployeeCode))
            .ToListAsync(cancellationToken);

        var matchedEmployees = employees
            .Where(employee => ContainsQuestionName(question, employee.Name) || ContainsQuestionName(question, employee.EmployeeCode))
            .Take(5)
            .ToList();
        var isSpecificEmployeeQuestion = matchedEmployees.Count > 0;
        var targetEmployees = isSpecificEmployeeQuestion ? matchedEmployees : employees;
        var employeeIds = targetEmployees.Select(employee => employee.Id).ToList();
        var today = FieldAttendanceClock.Now.Date;
        var todayDate = DateOnly.FromDateTime(today);
        var asOf = today.ToString("dd MMM yyyy");

        var context = new Dictionary<string, object?>
        {
            ["asOf"] = asOf,
            ["accessibleTeamSize"] = employees.Count,
            ["note"] = "This is the signed-in user's authorized scope. Use only the supplied records. Employee names, attendance, leave type/status/dates, and task title/status/priority/due date are included only when relevant. Leave reasons, contact details, location coordinates, biometrics, payroll, bank data, passwords, and documents are not available."
        };
        var exactAnswers = new List<string>();

        if (asksPeople && !asksAttendance && !asksLeave && !asksTasks)
        {
            context["employeeSummary"] = new { count = targetEmployees.Count };
            if (isSpecificEmployeeQuestion || HasAny(question, "who", "list", "names", "show", "kaun", "कौन"))
            {
                context["employees"] = targetEmployees.Select(employee => new { employee.Name })
                    .Take(isSpecificEmployeeQuestion ? 5 : 40).ToList();
            }
            if (isSpecificEmployeeQuestion)
                context["matchedEmployeeCount"] = matchedEmployees.Count;

            exactAnswers.Add(isSpecificEmployeeQuestion
                ? $"I found {matchedEmployees.Count} matching employee(s): {string.Join(", ", matchedEmployees.Select(employee => employee.Name))}."
                : HasAny(question, "who", "list", "show", "name", "kaun", "dikhao")
                    ? $"There are {targetEmployees.Count} active employees in your authorized scope. Names: {string.Join(", ", targetEmployees.Take(40).Select(employee => employee.Name))}."
                    : $"There are {targetEmployees.Count} active employees in your authorized scope.");
        }

        if (asksAttendance)
        {
            var tomorrow = today.AddDays(1);
            var logs = employeeIds.Count == 0
                ? new List<AssistantPunch>()
                : await db.AttendanceLogs.AsNoTracking()
                    .Where(log => log.EmployeeId.HasValue && employeeIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < tomorrow)
                    .Select(log => new AssistantPunch(log.EmployeeId!.Value, log.PunchTime))
                    .ToListAsync(cancellationToken);
            var punchesByEmployee = logs.GroupBy(log => log.EmployeeId)
                .ToDictionary(group => group.Key, group => group.Min(log => log.PunchTime));
            var lateIds = punchesByEmployee.Where(pair => pair.Value.TimeOfDay > new TimeSpan(10, 0, 0))
                .Select(pair => pair.Key).ToHashSet();
            var presentIds = punchesByEmployee.Keys.ToHashSet();
            var onLeaveIds = employeeIds.Count == 0
                ? new HashSet<int>()
                : (await db.LeaveRequests.AsNoTracking()
                    .Where(leave => employeeIds.Contains(leave.EmployeeId) && leave.Status == "Approved" && leave.FromDate <= todayDate && leave.ToDate >= todayDate)
                    .Select(leave => leave.EmployeeId)
                    .Distinct()
                    .ToListAsync(cancellationToken)).ToHashSet();
            var isWeeklyOff = AttendanceRules.IsWeeklyOff(todayDate);

            context["attendanceSummary"] = new
            {
                date = asOf,
                teamSize = targetEmployees.Count,
                present = presentIds.Count,
                late = lateIds.Count,
                onApprovedLeave = onLeaveIds.Count,
                absent = isWeeklyOff ? 0 : targetEmployees.Count(employee => !presentIds.Contains(employee.Id) && !onLeaveIds.Contains(employee.Id)),
                weeklyOff = isWeeklyOff
            };

            if (isSpecificEmployeeQuestion || HasAny(question, "who", "list", "team", "names", "kaun", "कौन", "show"))
            {
                context["attendance"] = targetEmployees.Select(employee => new
                {
                    employee.Name,
                    status = presentIds.Contains(employee.Id) ? "Present"
                        : onLeaveIds.Contains(employee.Id) ? "On approved leave"
                        : isWeeklyOff ? "Weekly off" : "No attendance recorded",
                    firstPunch = punchesByEmployee.TryGetValue(employee.Id, out var punch) ? punch.ToString("hh:mm tt") : null,
                    late = lateIds.Contains(employee.Id)
                }).Take(60).ToList();
            }

            var attendanceRows = targetEmployees.Select(employee => new
            {
                employee.Name,
                Status = presentIds.Contains(employee.Id) ? "Present"
                    : onLeaveIds.Contains(employee.Id) ? "On approved leave"
                    : isWeeklyOff ? "Weekly off" : "No attendance recorded",
                FirstPunch = punchesByEmployee.TryGetValue(employee.Id, out var punch) ? punch : (DateTime?)null,
                Late = lateIds.Contains(employee.Id)
            }).ToList();
            var attendanceSummary = $"Attendance for {asOf}: {presentIds.Count} present, {lateIds.Count} late (after 10:00 AM), {onLeaveIds.Count} on approved leave, {(isWeeklyOff ? 0 : attendanceRows.Count(row => row.Status == "No attendance recorded"))} with no attendance record."
                + (isWeeklyOff ? " It is a weekly off." : string.Empty);
            if (isSpecificEmployeeQuestion || HasAny(question, "who", "list", "team", "names", "kaun", "कौन", "show", "kaun present", "who is present"))
            {
                var lines = attendanceRows.Take(60).Select(row => $"{row.Name}: {row.Status}{(row.FirstPunch.HasValue ? $", first punch {row.FirstPunch.Value:hh:mm tt}" : string.Empty)}{(row.Late ? ", late" : string.Empty)}");
                exactAnswers.Add($"{attendanceSummary}\n{(attendanceRows.Count == 0 ? "No employees are in this authorized scope." : string.Join("\n", lines))}");
            }
            else
            {
                exactAnswers.Add(attendanceSummary);
            }
        }

        if (asksLeave)
        {
            var leaveQuery = db.LeaveRequests.AsNoTracking().Where(leave => employeeIds.Contains(leave.EmployeeId));
            var leaveStatistics = await leaveQuery.GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Count(),
                    Pending = group.Count(leave => leave.Status == "Pending"),
                    Approved = group.Count(leave => leave.Status == "Approved")
                }).SingleOrDefaultAsync(cancellationToken);
            var pendingLeaveCount = leaveStatistics?.Pending ?? 0;
            var approvedLeaveCount = leaveStatistics?.Approved ?? 0;
            var allLeaveCount = leaveStatistics?.Total ?? 0;
            var isAskingCurrentLeave = HasAny(question, "currently on leave", "on leave today", "on leave now", "who is on leave", "who's on leave", "currently on leave");
            var isAskingPendingLeave = HasAny(question, "pending", "waiting", "awaiting approval");
            var isAskingApprovedLeave = HasAny(question, "approved leave", "approved request", "approved requests");
            var requestedLeaves = isAskingCurrentLeave
                ? leaveQuery.Where(leave => leave.Status == "Approved" && leave.FromDate <= todayDate && leave.ToDate >= todayDate)
                : isAskingPendingLeave
                    ? leaveQuery.Where(leave => leave.Status == "Pending")
                    : isAskingApprovedLeave
                        ? leaveQuery.Where(leave => leave.Status == "Approved")
                    : isSpecificEmployeeQuestion
                        ? leaveQuery.Where(leave => employeeIds.Contains(leave.EmployeeId))
                        : leaveQuery;
            var currentApprovedLeaveCount = await leaveQuery
                .Where(leave => leave.Status == "Approved" && leave.FromDate <= todayDate && leave.ToDate >= todayDate)
                .Select(leave => leave.EmployeeId).Distinct().CountAsync(cancellationToken);
            var requestedLeaveCount = isAskingCurrentLeave ? currentApprovedLeaveCount
                : isAskingPendingLeave ? pendingLeaveCount
                : isAskingApprovedLeave ? approvedLeaveCount
                : isSpecificEmployeeQuestion ? await requestedLeaves.CountAsync(cancellationToken)
                : allLeaveCount;
            var needsLeaveList = isSpecificEmployeeQuestion || HasAny(question, "who", "list", "team", "names", "kaun", "कौन", "show", "request", "which");
            var leaveRows = await requestedLeaves.OrderByDescending(leave => leave.AppliedAtUtc)
                .Select(leave => new AssistantLeave(leave.Employee.FullName, leave.LeaveType, leave.FromDate, leave.ToDate, leave.Status))
                .Take(needsLeaveList ? 40 : 0).ToListAsync(cancellationToken);

            context["leaveSummary"] = new
            {
                total = requestedLeaveCount,
                pending = pendingLeaveCount,
                approved = approvedLeaveCount,
                currentApproved = currentApprovedLeaveCount
            };
            if (needsLeaveList)
                context["leaves"] = leaveRows.Select(leave => new { leave.EmployeeName, leave.Type, from = leave.From.ToString("dd MMM yyyy"), to = leave.To.ToString("dd MMM yyyy"), leave.Status }).ToList();

            var leaveDescription = isAskingCurrentLeave ? $"{requestedLeaveCount} employee(s) are on approved leave today ({asOf})."
                : isAskingPendingLeave ? $"There are {requestedLeaveCount} pending leave request(s) in your authorized scope."
                : $"Your authorized scope has {pendingLeaveCount} pending leave request(s), {approvedLeaveCount} approved request(s) in total, and {currentApprovedLeaveCount} employee(s) currently on approved leave.";
            if (needsLeaveList)
            {
                var leaveLines = leaveRows.Select(leave => $"{leave.EmployeeName}: {leave.Type}, {leave.Status}, {leave.From:dd MMM yyyy} to {leave.To:dd MMM yyyy}");
                exactAnswers.Add($"{leaveDescription}\n{(leaveRows.Count == 0 ? "No matching leave requests were found." : string.Join("\n", leaveLines))}");
            }
            else exactAnswers.Add(leaveDescription);
        }

        if (asksTasks)
        {
            var taskQuery = db.WorkTasks.AsNoTracking()
                .Where(task => managerEmployeeId.HasValue
                    ? task.ManagerId == managerEmployeeId.Value || employeeIds.Contains(task.AssigneeId)
                    : employeeIds.Contains(task.AssigneeId));
            if (isSpecificEmployeeQuestion)
                taskQuery = taskQuery.Where(task => employeeIds.Contains(task.AssigneeId));

            var taskStatistics = await taskQuery.GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Count(),
                    Open = group.Count(task => task.Status != "Completed"),
                    Overdue = group.Count(task => task.Status != "Completed" && task.DueDate < todayDate),
                    Completed = group.Count(task => task.Status == "Completed")
                }).SingleOrDefaultAsync(cancellationToken);
            var totalTasks = taskStatistics?.Total ?? 0;
            var openTasks = taskStatistics?.Open ?? 0;
            var overdueTasks = taskStatistics?.Overdue ?? 0;
            var completedTasks = taskStatistics?.Completed ?? 0;
            var needsTaskList = isSpecificEmployeeQuestion || HasAny(question, "who", "list", "team", "names", "kaun", "कौन", "show", "which", "what are");
            var tasks = needsTaskList
                ? await taskQuery.OrderBy(task => task.DueDate)
                    .Select(task => new AssistantTask(task.Title, task.Assignee.FullName, task.Status, task.Priority, task.DueDate))
                    .Take(40).ToListAsync(cancellationToken)
                : new List<AssistantTask>();

            context["taskSummary"] = new
            {
                total = totalTasks,
                open = openTasks,
                overdue = overdueTasks,
                completed = completedTasks
            };
            if (needsTaskList)
                context["tasks"] = tasks.Select(task => new { task.Title, task.Assignee, task.Status, task.Priority, dueDate = task.DueDate.ToString("dd MMM yyyy") }).ToList();

            var taskDescription = HasAny(question, "overdue", "late")
                ? $"There are {overdueTasks} overdue task(s) in your authorized scope as of {asOf}."
                : HasAny(question, "pending", "open", "remaining")
                    ? $"There are {openTasks} open task(s) and {overdueTasks} overdue in your authorized scope."
                    : $"Your authorized scope has {totalTasks} tasks: {openTasks} open, {completedTasks} completed, {overdueTasks} overdue.";
            if (needsTaskList)
            {
                var taskLines = tasks.Select(task => $"{task.Title} — {task.Assignee}: {task.Status}, {task.Priority} priority, due {task.DueDate:dd MMM yyyy}");
                exactAnswers.Add($"{taskDescription}\n{(tasks.Count == 0 ? "No matching tasks were found." : string.Join("\n", taskLines))}");
            }
            else exactAnswers.Add(taskDescription);
        }

        var serializedContext = JsonSerializer.Serialize(context);
        return Json(new
        {
            answer = exactAnswers.Count == 0 ? null : string.Join("\n\n", exactAnswers),
            context = exactAnswers.Count == 0 ? serializedContext : null
        });
    }

    private static bool HasAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsQuestionName(string question, string candidate)
    {
        var normalizedQuestion = $" {question.ToLowerInvariant().Replace('-', ' ')} ";
        var normalizedCandidate = $" {candidate.ToLowerInvariant().Replace('-', ' ')} ";
        if (normalizedQuestion.Contains(normalizedCandidate, StringComparison.Ordinal)) return true;
        return normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4)
            .Any(token => normalizedQuestion.Contains($" {token} ", StringComparison.Ordinal));
    }

    private sealed record AssistantEmployee(int Id, string Name, string EmployeeCode);
    private sealed record AssistantPunch(int EmployeeId, DateTime PunchTime);
    private sealed record AssistantLeave(string EmployeeName, string Type, DateOnly From, DateOnly To, string Status);
    private sealed record AssistantTask(string Title, string Assignee, string Status, string Priority, DateOnly DueDate);
}
