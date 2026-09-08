namespace VertexERP.Models;

public sealed record SalarySlipMonth(int Year, int Month, string Label, bool IsReleased);

public sealed class SalarySlipPageViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<SalarySlipMonth> Months { get; init; } = Array.Empty<SalarySlipMonth>();
}

public sealed class SalarySlipAdminViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string? Department { get; init; }
    public int? EmployeeId { get; init; }
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SalarySlipAdminRow> Employees { get; init; } = Array.Empty<SalarySlipAdminRow>();
}

public sealed class SalarySlipAdminRow
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public decimal GrossSalary { get; init; }
    public decimal StandardDeductions { get; init; }
    public decimal ApprovedLeaveDays { get; init; }
    public decimal LeaveDeduction { get; init; }
    public string? DeductionNote { get; init; }
    public bool IsGenerated { get; init; }
    public DateTime? GeneratedAtUtc { get; init; }
}
