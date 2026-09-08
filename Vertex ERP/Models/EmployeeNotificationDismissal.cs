using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class EmployeeNotificationDismissal
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(20)] public string SourceType { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public DateTime DismissedAtUtc { get; set; } = DateTime.UtcNow;
}
