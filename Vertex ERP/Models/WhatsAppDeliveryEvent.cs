using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class WhatsAppDeliveryEvent
{
    public long Id { get; set; }
    [MaxLength(64)] public string EventKey { get; set; } = "";
    [MaxLength(200)] public string RequestId { get; set; } = "";
    [MaxLength(200)] public string MetaMessageId { get; set; } = "";
    [MaxLength(64)] public string RecipientNumberHash { get; set; } = "";
    [MaxLength(24)] public string Status { get; set; } = "";
    [MaxLength(100)] public string? ErrorCode { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Applied { get; set; }
}
