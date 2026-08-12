using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public class QueryTicket
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }
    [Required, MaxLength(150)] public string Subject { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string Category { get; set; } = "General";
    [Required, MaxLength(1500)] public string Description { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Status { get; set; } = "Open";
    [MaxLength(1500)] public string? Resolution { get; set; }
    public int? ResolvedByUserId { get; set; }
    public AppUser? ResolvedByUser { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
