using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public class BankDetailUpdateRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(120)] public string AccountHolderName { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string BankName { get; set; } = string.Empty;
    [Required] public string ProtectedAccountNumber { get; set; } = string.Empty;
    [Required, MaxLength(4)] public string AccountLastFour { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string IfscCode { get; set; } = string.Empty;
    [MaxLength(120)] public string? BranchName { get; set; }
    [Required, MaxLength(30)] public string AccountType { get; set; } = "Savings";
    [MaxLength(20)] public string? PanNumber { get; set; }
    [MaxLength(30)] public string? UanNumber { get; set; }
    [MaxLength(30)] public string? EsicNumber { get; set; }
    [MaxLength(100)] public string? UpiId { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(500)] public string? HrNote { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}
