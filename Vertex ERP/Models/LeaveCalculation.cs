using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace VertexERP.Models;

public sealed class LeaveCalculation
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int Year { get; set; }
    public string MonthsJson { get; set; } = "[]";
    public decimal OpeningEL { get; set; }
    public decimal OpeningEW { get; set; }
    [ConcurrencyCheck] public int Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LeaveMonth
{
    [Range(1, 12)] public int Month { get; set; }
    [Range(typeof(decimal), "0", "31")] public decimal WorkingDays { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal AdjustedEL { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal AdjustedCL { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal AdjustedML { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal EarnedEW { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal AdjustedEW { get; set; }
}

public sealed class LeaveCalculationEdit
{
    public int EmployeeId { get; set; }
    [Range(2000, 2100)] public int Year { get; set; }
    public int Revision { get; set; }
    public List<int> BalanceRevisions { get; set; } = [];
    [Range(typeof(decimal), "0", "3660")] public decimal OpeningEL { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal OpeningEW { get; set; }
    public List<LeaveMonth> Months { get; set; } = [];
    public string EmployeeName { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public List<ManualLeaveBalance> Balances { get; set; } = [];
    public decimal? PreviousEL { get; set; }
    public decimal? PreviousEW { get; set; }
    public bool HasSavedCalculation { get; set; }
    public bool ConfirmReplaceManual { get; set; }
}
