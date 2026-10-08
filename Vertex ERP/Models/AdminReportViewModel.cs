namespace VertexERP.Models;

public sealed class AdminReportViewModel
{
    public string? ReportType { get; init; }
    public string ReportTitle { get; init; } = "Select a report";
    public int? DepartmentId { get; init; }
    public int? EmployeeId { get; init; }
    public int? ManagerId { get; init; }
    public int? ProjectId { get; init; }
    public string? Status { get; init; }
    public string FilterSummary { get; init; } = "All records";
    public IReadOnlyList<ErpProject> Projects { get; init; } = [];
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
