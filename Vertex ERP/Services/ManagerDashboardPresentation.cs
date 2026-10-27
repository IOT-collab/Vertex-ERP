using VertexERP.Models;

namespace VertexERP.Services;

public sealed record ManagerWorkload(int Completed, int InProgress, int Pending, int Overdue)
{
    public int Total => Completed + InProgress + Pending + Overdue;
}

public static class ManagerDashboardPresentation
{
    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "V" : (parts[0][..1] + (parts.Length > 1 ? parts[^1][..1] : "")).ToUpperInvariant();
    }
    public static string Greeting(DateTimeOffset utc) => FieldAttendanceClock.IndiaTime(utc).Hour switch
    {
        < 12 => "Good Morning",
        < 17 => "Good Afternoon",
        _ => "Good Evening"
    };
    public static DateOnly IndiaDate(DateTime utc) => DateOnly.FromDateTime(FieldAttendanceClock.IndiaTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))));
    public static bool IsCompleted(WorkTask task) => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    public static bool IsOverdue(WorkTask task, DateOnly today) => !IsCompleted(task) && task.DueDate < today;
    public static ManagerWorkload Workload(IEnumerable<WorkTask> tasks, DateOnly today, string period)
    {
        var first = period == "month" ? new DateOnly(today.Year, today.Month, 1)
            : period == "week" ? today.AddDays(-(((int)today.DayOfWeek + 6) % 7)) : DateOnly.MinValue;
        var last = period == "month" ? first.AddMonths(1) : period == "week" ? first.AddDays(7) : DateOnly.MaxValue;
        var rows = tasks.Where(x => period == "all" || (IndiaDate(x.CreatedAtUtc) >= first && IndiaDate(x.CreatedAtUtc) < last)).ToList();
        return new(rows.Count(IsCompleted), rows.Count(x => !IsCompleted(x) && !IsOverdue(x, today) && x.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase)),
            rows.Count(x => !IsCompleted(x) && !IsOverdue(x, today) && !x.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase)), rows.Count(x => IsOverdue(x, today)));
    }
    public static bool AwaitingAction(LeaveRequest request, bool managerScope) => request.Status == "Pending" && request.ApprovalLevel == (managerScope ? "Manager" : "HR/Admin");
}
