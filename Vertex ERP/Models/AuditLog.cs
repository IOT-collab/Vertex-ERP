using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string ActorId { get; set; } = "";
    [MaxLength(200)] public string ActorName { get; set; } = "System";
    [MaxLength(100)] public string ActorRole { get; set; } = "System";
    [MaxLength(100)] public string EntityType { get; set; } = "";
    [MaxLength(200)] public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
