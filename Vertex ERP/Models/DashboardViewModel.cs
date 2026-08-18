namespace VertexERP.Models;

public class DashboardViewModel
{
    public int TotalWorkforce { get; init; }
    public int ActiveWorkforce { get; init; }
    public int PresentToday { get; init; }
    public int LateToday { get; init; }
    public int AbsentToday { get; init; }
    public int OpenTasks { get; init; }
    public int OverdueTasks { get; init; }
    public int CompletedTasks { get; init; }
    public int TaskProgressPercentage { get; init; }
    public IReadOnlyList<DashboardEmployeeRow> RecentEmployees { get; init; } = [];
    public IReadOnlyList<DashboardDepartmentMetric> Departments { get; init; } = [];
    public IReadOnlyList<DashboardDayMetric> WeeklyAttendance { get; init; } = [];
    public IReadOnlyList<DashboardActivityItem> RecentActivity { get; init; } = [];
}

public record DashboardEmployeeRow(int Id, string EmployeeId, string FullName, string Email, string Department, string Designation, bool IsActive, string? PhotoPath);
public record DashboardDepartmentMetric(string Name, int Count);
public record DashboardDayMetric(string Label, int Count);
public record DashboardActivityItem(string Title, string Detail, DateTime OccurredAt);

public sealed class HrmsDashboardViewModel
{
    public int TotalEmployees { get; init; }
    public int PresentToday { get; init; }
    public int OnLeaveToday { get; init; }
    public int AbsentToday { get; init; }
}
