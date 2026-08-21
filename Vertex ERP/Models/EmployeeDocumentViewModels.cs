using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace VertexERP.Models;

public sealed class EmployeeDocumentFormViewModel
{
    [Required] public int? EmployeeId { get; set; }
    [Required, StringLength(120)] public string EmployeeName { get; set; } = string.Empty;
    [DataType(DataType.Date)] public DateOnly? DateOfBirth { get; set; }
    [DataType(DataType.Date)] public DateOnly? JoiningDate { get; set; }
    [Required, StringLength(10), RegularExpression(@"^\d{10}$", ErrorMessage = "Mobile number must contain exactly 10 digits.")] public string Mobile { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Designation { get; set; } = string.Empty;
    [StringLength(80)] public string Department { get; set; } = string.Empty;
    [Required, StringLength(120)] public string ManagerName { get; set; } = string.Empty;
    [Required] public string DocumentType { get; set; } = "Offer Letter";
    [Required, DataType(DataType.Date)] public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [StringLength(80)] public string? NewDesignation { get; set; }
    [StringLength(1000)] public string? AdditionalNotes { get; set; }
    [StringLength(10)] public string? PanNumber { get; set; }
    [StringLength(100)] public string? WorkLocation { get; set; }
    [StringLength(40)] public string? AnnualCtc { get; set; }
    [StringLength(40)] public string? MonthlyGross { get; set; }
    [StringLength(40)] public string? ProbationPeriod { get; set; }
    [StringLength(100)] public string? BankName { get; set; }
    [StringLength(18), RegularExpression(@"^$|^\d{8,18}$", ErrorMessage = "Bank account number must contain 8 to 18 digits.")] public string? BankAccountNumber { get; set; }
    [StringLength(30)] public string? PfNumber { get; set; }
    [StringLength(30)] public string? PfUan { get; set; }
    [Range(0, 100000000)] public decimal? BasicSalary { get; set; }
    [Range(0, 100000000)] public decimal? HouseRentAllowance { get; set; }
    [Range(0, 100000000)] public decimal? ConveyanceAllowance { get; set; }
    [Range(0, 100000000)] public decimal? SpecialAllowance { get; set; }
    [Range(0, 100000000)] public decimal? ProvidentFund { get; set; }
    [Range(0, 100000000)] public decimal? ProfessionalTax { get; set; }
    [Range(0, 100000000)] public decimal? Tds { get; set; }
    [Range(0, 100000000)] public decimal? OtherDeductions { get; set; }
    [StringLength(40)] public string? IncrementType { get; set; }
    [StringLength(40)] public string? CurrentCompensation { get; set; }
    [StringLength(40)] public string? RevisedCompensation { get; set; }
    [StringLength(20)] public string? IncrementPercentage { get; set; }
    [StringLength(300)] public string? PromotionReason { get; set; }
    [DataType(DataType.Date)] public DateOnly? ResignationDate { get; set; }
    [StringLength(40)] public string? ClearanceStatus { get; set; }
    public IReadOnlyList<EmployeeDocumentEmployeeOption> Employees { get; set; } = Array.Empty<EmployeeDocumentEmployeeOption>();
}

public sealed class EmployeeDocumentEmployeeOption
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? DateOfBirth { get; init; }
    public string? JoiningDate { get; init; }
    public string Mobile { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string ManagerName { get; init; } = string.Empty;
}

public sealed class EmployeeDocumentUploadViewModel
{
    [Required] public int? EmployeeId { get; set; }
    [Required, StringLength(80)] public string DocumentType { get; set; } = string.Empty;
    [Required, StringLength(160)] public string DocumentName { get; set; } = string.Empty;
    [Required] public IFormFile? File { get; set; }
    [DataType(DataType.Date)] public DateOnly? ExpiryDate { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public IReadOnlyList<EmployeeDocumentEmployeeOption> Employees { get; set; } = Array.Empty<EmployeeDocumentEmployeeOption>();
}
