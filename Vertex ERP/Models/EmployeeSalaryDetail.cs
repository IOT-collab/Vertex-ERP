using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class EmployeeSalaryDetail
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Range(0, 100000000)] public decimal BasicSalary { get; set; }
    [Range(0, 100000000)] public decimal HouseRentAllowance { get; set; }
    [Range(0, 100000000)] public decimal ConveyanceAllowance { get; set; }
    [Range(0, 100000000)] public decimal SpecialAllowance { get; set; }
    [Range(0, 100000000)] public decimal ProvidentFund { get; set; }
    [Range(0, 100000000)] public decimal ProfessionalTax { get; set; }
    [Range(0, 100000000)] public decimal Tds { get; set; }
    [Range(0, 100000000)] public decimal OtherDeductions { get; set; }
    [MaxLength(50)] public string? PfNumber { get; set; }
    [MaxLength(30)] public string? PfUan { get; set; }
    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal GrossSalary => BasicSalary + HouseRentAllowance + ConveyanceAllowance + SpecialAllowance;
    public decimal TotalDeductions => ProvidentFund + ProfessionalTax + Tds + OtherDeductions;
    public decimal NetSalary => GrossSalary - TotalDeductions;
}
