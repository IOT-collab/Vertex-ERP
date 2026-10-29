using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class FeedbackDocument
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Instructions { get; set; }
    public int PublishedByUserId { get; set; }
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<FeedbackDocumentAsset> Assets { get; set; } = new List<FeedbackDocumentAsset>();
    public ICollection<FeedbackDocumentSubmission> Submissions { get; set; } = new List<FeedbackDocumentSubmission>();
}

public sealed class FeedbackDocumentSubmission
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public FeedbackDocument Document { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int SubmittedByUserId { get; set; }
    [MaxLength(2000)] public string? Comment { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public ICollection<FeedbackDocumentAsset> Assets { get; set; } = new List<FeedbackDocumentAsset>();
}

public sealed class FeedbackDocumentAsset
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public FeedbackDocument Document { get; set; } = null!;
    public int? SubmissionId { get; set; }
    public FeedbackDocumentSubmission? Submission { get; set; }
    public int Revision { get; set; }
    [Required, MaxLength(260)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PublishFeedbackDocumentForm
{
    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Instructions { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}
public sealed class SubmitFeedbackDocumentForm
{
    [StringLength(2000)] public string? Comment { get; set; }
    public List<IFormFile> Files { get; set; } = [];
}
public sealed class FeedbackDocumentCard
{
    public FeedbackDocument Document { get; init; } = null!;
    public FeedbackDocumentSubmission? OwnSubmission { get; init; }
    public int SubmittedCount { get; init; }
    public int ReachedCount { get; init; }
    public int PendingCount => Math.Max(0, ReachedCount - SubmittedCount);
}
public sealed class FeedbackDocumentsViewModel
{
    public bool IsManagement { get; init; }
    public IReadOnlyList<FeedbackDocumentCard> Documents { get; init; } = [];
    public int TotalEmployees { get; init; }
    public int? SelectedDocumentId { get; init; }
    public FeedbackDocumentCard? SelectedDocument => Documents.FirstOrDefault(x => x.Document.Id == SelectedDocumentId) ?? Documents.FirstOrDefault();
}
public sealed record FeedbackDocumentEmployeeRow(Employee Employee, FeedbackDocumentSubmission? Submission);
public sealed class FeedbackDocumentDetailsViewModel
{
    public bool CanSubmit { get; init; }
    public bool IsManagement { get; init; }
    public FeedbackDocument Document { get; init; } = null!;
    public FeedbackDocumentSubmission? OwnSubmission { get; init; }
    public IReadOnlyList<FeedbackDocumentEmployeeRow> Employees { get; init; } = [];
    public IReadOnlyList<FeedbackDocument> Documents { get; init; } = [];
    public IReadOnlyList<string> Departments { get; init; } = [];
    public int TotalEmployees { get; init; }
    public int SubmittedCount { get; init; }
    public int PendingCount => TotalEmployees - SubmittedCount;
    public string? Search { get; init; }
    public string? Department { get; init; }
    public string? Status { get; init; }
}
