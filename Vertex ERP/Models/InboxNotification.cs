using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;

public sealed class NotificationReadReceipt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    [Required, MaxLength(120)] public string EventKey { get; set; } = "";
    public DateTime ReadAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed record InboxNotification(string Key, string Category, string Title, string Detail, string Url, DateTime AtUtc, bool IsRead = false);
public sealed class NotificationInboxViewModel
{
    public IReadOnlyList<InboxNotification> Items { get; init; } = [];
    public int UnreadCount { get; init; }
    public string? Category { get; init; }
    public bool UnreadOnly { get; init; }
    public int Page { get; init; } = 1;
    public int Total { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / 30d));
}
