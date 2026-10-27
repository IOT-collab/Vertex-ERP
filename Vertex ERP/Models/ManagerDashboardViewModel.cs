namespace VertexERP.Models;

public sealed class ManagerDashboardViewModel
{
    public Employee? Viewer { get; init; }
    public bool IsManagerScope { get; init; }
    public IReadOnlyList<WorkTask> OwnTasks { get; init; } = Array.Empty<WorkTask>();
    public int PendingApprovals => LeaveRequests.Count(x => VertexERP.Services.ManagerDashboardPresentation.AwaitingAction(x, IsManagerScope)) + ExpenseClaims.Count(x => x.Status == "Pending");
    public int CompletedThisMonth => Tasks.Count(task => VertexERP.Services.ManagerDashboardPresentation.IsCompleted(task)
        && VertexERP.Services.ManagerDashboardPresentation.IndiaDate(task.UpdatedAtUtc ?? task.CreatedAtUtc).Year == VertexERP.Services.FieldAttendanceClock.Now.Year
        && VertexERP.Services.ManagerDashboardPresentation.IndiaDate(task.UpdatedAtUtc ?? task.CreatedAtUtc).Month == VertexERP.Services.FieldAttendanceClock.Now.Month);
    public IReadOnlyList<ErpProject> Projects { get; init; } = Array.Empty<ErpProject>();
    public IReadOnlyList<ProjectEmployee> ProjectAssignments { get; init; } = Array.Empty<ProjectEmployee>();
    public IReadOnlyList<Employee> Managers { get; init; } = Array.Empty<Employee>();
    public IReadOnlyList<Employee> TeamMembers { get; init; } = Array.Empty<Employee>();
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
    public IReadOnlyList<LeaveRequest> LeaveRequests { get; init; } = Array.Empty<LeaveRequest>();
    public IReadOnlyList<QueryTicket> QueryTickets { get; init; } = Array.Empty<QueryTicket>();
    public IReadOnlyList<ExpenseClaim> ExpenseClaims { get; init; } = Array.Empty<ExpenseClaim>();
    public IReadOnlyList<DashboardNotification> Notifications { get; init; } = Array.Empty<DashboardNotification>();
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Count(task => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int PendingTasks => Tasks.Count(task => !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int InProgressTasks => Tasks.Count(task => task.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase));
    public int OverdueTasks => Tasks.Count(task => !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && task.DueDate < DateOnly.FromDateTime(VertexERP.Services.FieldAttendanceClock.Now));
    public int PendingLeaveRequests => LeaveRequests.Count(request => request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int OpenQueryTickets => QueryTickets.Count(ticket => !ticket.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) && !ticket.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase));
    public int NotificationCount => Notifications.Count;
}

public sealed record DashboardNotification(string Type, string Title, string Detail, string Url, DateTime CreatedAtUtc);
