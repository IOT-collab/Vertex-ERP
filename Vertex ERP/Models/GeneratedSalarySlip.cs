using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class GeneratedSalarySlip
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal HouseRentAllowance { get; set; }
    public decimal ConveyanceAllowance { get; set; }
    public decimal SpecialAllowance { get; set; }
    public decimal ProvidentFund { get; set; }
    public decimal ProfessionalTax { get; set; }
    public decimal Tds { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal LeaveDeduction { get; set; }
    public decimal ApprovedLeaveDays { get; set; }
    [MaxLength(300)] public string? DeductionNote { get; set; }
    [MaxLength(50)] public string? PfNumber { get; set; }
    [MaxLength(30)] public string? PfUan { get; set; }
    public int GeneratedByUserId { get; set; }
    public AppUser GeneratedByUser { get; set; } = null!;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal GrossSalary => BasicSalary + HouseRentAllowance + ConveyanceAllowance + SpecialAllowance;
    public decimal TotalDeductions => ProvidentFund + ProfessionalTax + Tds + OtherDeductions + LeaveDeduction;
    public decimal NetSalary => GrossSalary - TotalDeductions;
}
