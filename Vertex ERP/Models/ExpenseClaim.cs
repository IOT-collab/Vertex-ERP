using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ExpenseClaim
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }
    public bool RequiresHrApproval { get; set; }
    [ConcurrencyCheck, MaxLength(20)] public string WorkflowStage { get; set; } = "Manager";
    [MaxLength(20)] public string? ManagerDecision { get; set; }
    [MaxLength(500)] public string? ManagerNote { get; set; }
    public int? ManagerDecidedByUserId { get; set; }
    public DateTime? ManagerDecidedAtUtc { get; set; }
    public int? ForwardedToEmployeeId { get; set; }
    public Employee? ForwardedToEmployee { get; set; }
    public int? ForwardedByUserId { get; set; }
    public DateTime? ForwardedAtUtc { get; set; }
    [MaxLength(20)] public string? HrDecision { get; set; }
    [MaxLength(20)] public string? RecipientDecision { get; set; }
    [MaxLength(1000)] public string? RecipientReason { get; set; }
    public DateTime? RecipientRespondedAtUtc { get; set; }
    public int? RecipientRespondedByUserId { get; set; }
    [MaxLength(260)] public string? ResponseStoredFileName { get; set; }
    [MaxLength(260)] public string? ResponseOriginalFileName { get; set; }
    [MaxLength(100)] public string? ResponseContentType { get; set; }
    public long? ResponseFileSize { get; set; }


    [Required, MaxLength(40)] public string Category { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "Pending";
    [Required, MaxLength(260)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public int? DecidedByUserId { get; set; }
    public AppUser? DecidedByUser { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    [MaxLength(500)] public string? DecisionNote { get; set; }
}

public sealed class SubmitExpenseClaimViewModel
{
    [Required, MaxLength(40)] public string Category { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    [Required] public DateOnly? ExpenseDate { get; set; }
    [Range(0.01, 10000000)] public decimal Amount { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    [Required] public IFormFile? ReceiptFile { get; set; }
}

public sealed class RespondToExpenseClaimViewModel
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, RegularExpression("^(Approved|Rejected)$")] public string Decision { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public IFormFile? ResponseFile { get; set; }
}

public sealed class ExpenseClaimPageViewModel
{
    public int? CurrentEmployeeId { get; init; }
    public bool CanReceiveForwardedClaims { get; init; }
    public bool HasReportingManager { get; init; }
    public string? ReportingManagerName { get; init; }

    public IReadOnlyList<ExpenseAdvance> Advances { get; init; } = Array.Empty<ExpenseAdvance>();
    public IReadOnlyList<Employee> AdvanceEmployees { get; init; } = Array.Empty<Employee>();
    public string Mode { get; init; } = "Employee";
    public IReadOnlyList<ExpenseClaim> Claims { get; init; } = Array.Empty<ExpenseClaim>();
    public decimal TotalAmount => Claims.Sum(x => x.Amount);
    public decimal PendingAmount => Claims.Where(x => x.WorkflowStage != "Completed").Sum(x => x.Amount);
    public decimal ApprovedAmount => Claims.Where(x => x.WorkflowStage == "Completed" && x.RecipientDecision == "Approved").Sum(x => x.Amount);
    public decimal RejectedAmount => Claims.Where(x => x.WorkflowStage == "Completed" && x.RecipientDecision == "Rejected").Sum(x => x.Amount);
}
