using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class EmployeeDocument
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(80)] public string DocumentType { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string DocumentName { get; set; } = string.Empty;
    [Required, MaxLength(260)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(120)] public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
