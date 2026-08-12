namespace VertexERP.Models;

public sealed class ManagerDashboardViewModel
{
    public IReadOnlyList<Employee> Managers { get; init; } = Array.Empty<Employee>();
    public IReadOnlyList<Employee> TeamMembers { get; init; } = Array.Empty<Employee>();
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
    public IReadOnlyList<LeaveRequest> LeaveRequests { get; init; } = Array.Empty<LeaveRequest>();
    public IReadOnlyList<QueryTicket> QueryTickets { get; init; } = Array.Empty<QueryTicket>();
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Count(task => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int PendingTasks => Tasks.Count(task => !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int InProgressTasks => Tasks.Count(task => task.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase));
    public int OverdueTasks => Tasks.Count(task => !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && task.DueDate < DateOnly.FromDateTime(DateTime.Today));
    public int PendingLeaveRequests => LeaveRequests.Count(request => request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int OpenQueryTickets => QueryTickets.Count(ticket => !ticket.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) && !ticket.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase));
}
