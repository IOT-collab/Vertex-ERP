using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class SalarySlipRevisionViewModel
{
    public int Id { get; set; }
    public long Version { get; set; }
    public string EmployeeName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    [Required, StringLength(300)] public string Reason { get; set; } = "";
    [StringLength(300)] public string? DeductionNote { get; set; }
    public Dictionary<string, decimal> Values { get; set; } = new();
    public IReadOnlyList<AuditLog> History { get; set; } = Array.Empty<AuditLog>();
    public static readonly string[] Fields = ["SalaryDays", "BasicSalary", "HouseRentAllowance", "ConveyanceAllowance", "SpecialAllowance", "ProvidentFund", "ProfessionalTax", "Tds", "OtherDeductions", "LeaveDeduction"];
    public static Dictionary<string, decimal> Snapshot(GeneratedSalarySlip slip) => Fields.ToDictionary(name => name,
        name => (decimal?)typeof(GeneratedSalarySlip).GetProperty(name)!.GetValue(slip) ?? 0m);
}
