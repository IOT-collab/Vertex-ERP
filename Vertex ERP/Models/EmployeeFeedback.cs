using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class EmployeeFeedback
{
    public int Id { get; set; }
    public int? SubmittedByUserId { get; set; }
    public AppUser? SubmittedByUser { get; set; }
    [Required, MaxLength(100)] public string SenderName { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string SenderRole { get; set; } = string.Empty;
    [MaxLength(30)] public string? EmployeeCode { get; set; }
    [MaxLength(200)] public string? Department { get; set; }
    [Required, MaxLength(150)] public string Subject { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Message { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FeedbackForm
{
    [Required(ErrorMessage = "Please enter a feedback subject."), StringLength(150, ErrorMessage = "Subject cannot exceed 150 characters.")]
    public string Subject { get; set; } = string.Empty;
    [Required(ErrorMessage = "Please enter your feedback."), StringLength(2000, ErrorMessage = "Feedback cannot exceed 2000 characters.")]
    public string Message { get; set; } = string.Empty;
}

public sealed class FeedbackInboxViewModel
{
    public IReadOnlyList<EmployeeFeedback> Items { get; init; } = Array.Empty<EmployeeFeedback>();
    public int Page { get; init; }
    public int Total { get; init; }
    public int PageSize { get; init; } = 25;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public string? Search { get; init; }
}
