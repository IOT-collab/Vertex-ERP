using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class MyBankDetailsViewModel { public Employee Employee { get; init; } = null!; public EmployeeBankDetail? BankDetail { get; init; } public IReadOnlyList<BankDetailUpdateRequest> Requests { get; init; } = Array.Empty<BankDetailUpdateRequest>(); public BankDetailRequestViewModel UpdateRequest { get; init; } = new(); }
public sealed class BankDetailRequestViewModel
{
    [Required, StringLength(120)] public string AccountHolderName { get; set; } = string.Empty;
    [Required, StringLength(120)] public string BankName { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[0-9]{8,20}$")] public string AccountNumber { get; set; } = string.Empty;
    [Required, Compare(nameof(AccountNumber))] public string ConfirmAccountNumber { get; set; } = string.Empty;
    [Required, RegularExpression(@"^[A-Za-z]{4}0[A-Za-z0-9]{6}$")] public string IfscCode { get; set; } = string.Empty;
    [StringLength(120)] public string? BranchName { get; set; }
    [Required] public string AccountType { get; set; } = "Savings";
    [StringLength(20)] public string? PanNumber { get; set; }
    [StringLength(30)] public string? UanNumber { get; set; }
    [StringLength(30)] public string? EsicNumber { get; set; }
    [StringLength(100)] public string? UpiId { get; set; }
}
public sealed class BankApprovalViewModel { public IReadOnlyList<BankDetailUpdateRequest> Requests { get; init; } = Array.Empty<BankDetailUpdateRequest>(); }
