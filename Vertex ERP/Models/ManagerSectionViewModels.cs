namespace VertexERP.Models;

public sealed class ManagerAttendanceViewModel
{
    public IReadOnlyList<Employee> TeamMembers { get; init; } = Array.Empty<Employee>();
    public IReadOnlySet<int> PresentIds { get; init; } = new HashSet<int>();
    public IReadOnlySet<int> OnLeaveIds { get; init; } = new HashSet<int>();
    public int Present => PresentIds.Count;
    public int OnLeave => OnLeaveIds.Count;
    public int Absent => TeamMembers.Count(employee => !PresentIds.Contains(employee.Id) && !OnLeaveIds.Contains(employee.Id));
}

public sealed class ManagerSectionViewModel
{
    public IReadOnlyList<LeaveRequest> LeaveRequests { get; init; } = Array.Empty<LeaveRequest>();
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
}
