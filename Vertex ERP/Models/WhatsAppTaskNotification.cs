using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;

public sealed class WhatsAppTaskNotification
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public WorkTask Task { get; set; } = null!;
    public int AssigneeId { get; set; }
    [MaxLength(24)] public string Status { get; set; } = "Pending";
    [MaxLength(24)] public string DeliveryStatus { get; set; } = "Pending";
    [MaxLength(64)] public string? RecipientNumberHash { get; set; }
    [MaxLength(200)] public string? MetaMessageId { get; set; }
    public DateTime? DeliveryUpdatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    [MaxLength(100)] public string? DeliveryErrorCode { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public int? HttpStatus { get; set; }
    [MaxLength(200)] public string? ProviderMessageId { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
}
