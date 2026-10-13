using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ExpenseAdvance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(150)] public string Purpose { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    [Required, MaxLength(30)] public string PaymentMode { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string TransactionId { get; set; } = string.Empty;
    public DateTime PaidAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public int RecordedByUserId { get; set; }
    [Required, MaxLength(260)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    [ConcurrencyCheck, MaxLength(30)] public string ConfirmationStatus { get; set; } = "Pending";
    public decimal? ReceivedAmount { get; set; }
    [MaxLength(500)] public string? EmployeeNote { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public int? RespondedByUserId { get; set; }
}

public sealed class RecordExpenseAdvanceViewModel
{
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [Required, MaxLength(150)] public string Purpose { get; set; } = string.Empty;
    [Range(0.01, 10000000)] public decimal Amount { get; set; }
    [Required, MaxLength(30)] public string PaymentMode { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string TransactionId { get; set; } = string.Empty;
    [Required] public DateTime? PaidAtIst { get; set; }
    [Required] public IFormFile? ReceiptFile { get; set; }
}

public sealed class ConfirmExpenseAdvanceViewModel
{
    public int Id { get; set; }
    [Required] public string Response { get; set; } = string.Empty;
    [Range(0, 10000000)] public decimal? ReceivedAmount { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
