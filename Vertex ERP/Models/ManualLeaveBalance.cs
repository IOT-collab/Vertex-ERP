using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace VertexERP.Models;
public sealed class ManualLeaveBalance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int Year { get; set; }
    [MaxLength(2)] public string Category { get; set; } = "";
    public decimal TotalDays { get; set; }
    public decimal UsedDays { get; set; }
    [NotMapped] public decimal AvailableDays => TotalDays - UsedDays;
    [MaxLength(500)] public string? Notes { get; set; }
    [ConcurrencyCheck] public int Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed class ManualLeaveCategoryEdit
{
    [Required, RegularExpression("^(EL|CL|ML|EW)$")] public string Category { get; set; } = "";
    [Range(typeof(decimal), "0", "3660")] public decimal? UsedDays { get; set; }
    [Range(typeof(decimal), "0", "3660")] public decimal? AvailableDays { get; set; }
    public int Revision { get; set; }
}
public sealed class ManualLeaveEmployeeEdit
{
    public int EmployeeId { get; set; }
    [Range(2000, 2100)] public int Year { get; set; }
    public List<ManualLeaveCategoryEdit> Categories { get; set; } = [];
}
public sealed class ManualLeavePage
{
    public static readonly string[] Categories = ["EL", "CL", "ML", "EW"];
    public List<Employee> Employees { get; set; } = [];
    public List<ManualLeaveBalance> Balances { get; set; } = [];
    public int Year { get; set; } = DateTime.Today.Year;
    public string? Search { get; set; }
    public ManualLeaveEmployeeEdit? Draft { get; set; }
}
