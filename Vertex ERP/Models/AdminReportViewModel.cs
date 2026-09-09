namespace VertexERP.Models;

public sealed class AdminReportViewModel
{
    public string? ReportType { get; init; }
    public string ReportTitle { get; init; } = "Select a report";
    public int? DepartmentId { get; init; }
    public int? EmployeeId { get; init; }
    public int? ManagerId { get; init; }
    public DateOnly? Date { get; init; }
    public string? Month { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public IReadOnlyList<Department> Departments { get; init; } = [];
    public IReadOnlyList<Employee> Employees { get; init; } = [];
    public IReadOnlyList<Employee> Managers { get; init; } = [];
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];
    public int PresentCount { get; init; }
    public int AbsentCount { get; init; }
    public int LateCount { get; init; }
    public bool HasSelection => !string.IsNullOrWhiteSpace(ReportType);
}

public static class AdminReportTypes
{
    public static readonly IReadOnlyList<(string Value, string Label)> All =
    [
        ("department-details", "Department Details"),
        ("department-manager", "Department Manager"),
        ("employee-list", "Total Employees and Complete Employee List"),
        ("employee-designation", "Employee Designation"),
        ("employee-tasks", "Employee-wise Assigned Tasks and Status"),
        ("manager-projects", "Manager-wise Projects and Status"),
        ("employee-attendance", "Employee-wise Attendance"),
        ("department-attendance", "Department-wise Attendance"),
        ("daily-attendance", "Selected Date Attendance Report"),
        ("monthly-attendance", "Selected Month Attendance Report"),
        ("attendance-summary", "Present, Absent and Late Summary"),
        ("punch-report", "Check-in, Check-out and Punch Count")
    ];

    public static string Label(string? value) => All.FirstOrDefault(item => item.Value == value).Label ?? "ERP Report";
}
