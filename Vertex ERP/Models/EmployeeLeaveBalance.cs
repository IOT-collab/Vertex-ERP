using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class EmployeeLeaveBalance
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int Year { get; set; }
    public decimal TotalDays { get; set; }
    public decimal UsedAdjustment { get; set; }
    [ConcurrencyCheck] public int Revision { get; set; }
}

public sealed class LeaveBalanceEdit
{
    public int EmployeeId { get; set; }
    [Range(2000, 2100)] public int Year { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal TotalDays { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal UsedDays { get; set; }
    public int Revision { get; set; }
    public decimal ApprovedDays { get; set; }
}

public sealed record LeaveBalanceRow(Employee Employee, decimal TotalDays, decimal UsedDays, decimal ApprovedDays, decimal PendingDays, int Revision)
{
    public decimal RemainingDays => TotalDays - UsedDays;
    public List<LeaveRequest> Requests { get; init; } = [];
    public Dictionary<string, decimal> CategoryUsed { get; init; } = [];
}

public sealed class LeaveBalancePage
{
    public int Year { get; set; }
    public string? Search { get; set; }
    public string? Department { get; set; }
    public List<string> Departments { get; set; } = [];
    public List<LeaveBalanceRow> Rows { get; set; } = [];
    public List<LeaveBalanceRow> SummaryRows { get; set; } = [];
    public List<Employee> Employees { get; set; } = [];
    public string Filter { get; set; } = "total";
    public string? Category { get; set; }
}

public sealed class ManageLeaveBalance
{
    [Range(2000, 2100)] public int Year { get; set; }
    public int? EmployeeId { get; set; }
    public bool AllEmployees { get; set; }
    [Required, RegularExpression("^(total|used)$")] public string Field { get; set; } = "total";
    [Required, RegularExpression("^(add|subtract|set)$")] public string Operation { get; set; } = "add";
    [Range(typeof(decimal), "0", "3660")] public decimal Days { get; set; }
}
