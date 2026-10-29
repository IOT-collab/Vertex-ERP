using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
namespace VertexERP.Services;

public static class Msg91Delivery
{
    public const string TokenHeader = "X-Vertex-Msg91-Webhook-Token";
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Authorized(Msg91WhatsAppOptions config, string? token) => config.WebhooksEnabled
        && config.WebhookToken.Length >= 32 && !string.IsNullOrEmpty(token)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(config.WebhookToken)), SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static WhatsAppDeliveryEvent? Parse(JsonElement root, Msg91WhatsAppOptions config)
    {
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Text("direction") != "1" || Text("integratedNumber") != config.IntegratedNumber || Text("templateName") != config.TemplateName) return null;
        var language = Text("templateLanguage");
        if (language != config.Language) return null;
        var status = Text("eventName").ToLowerInvariant() switch { "sent" => "Sent", "delivered" => "Delivered", "read" => "Read", "failed" => "Failed", _ => "" };
        var id = Text("requestId"); var uuid = Text("uuid"); var phone = Msg91WhatsAppService.NormalizePhone(Text("customerNumber"));
        var timestamp = Text("ts");
        // Require an explicit offset, rather than interpreting provider time in the server timezone.
        if (status == "" || string.IsNullOrWhiteSpace(id) || id.Length > 200 || uuid.Length > 200 || phone == null
            || !System.Text.RegularExpressions.Regex.IsMatch(timestamp, @"(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurred)) return null;
        var reasonCode = Text("reason").Split(':', 2)[0].Trim();
        return new WhatsAppDeliveryEvent { EventKey = Hash(id + "|" + uuid + "|" + phone + "|" + status + "|" + occurred.UtcDateTime.Ticks),
            RequestId = id, MetaMessageId = uuid, RecipientNumberHash = Hash(phone), Status = status, OccurredAtUtc = occurred.UtcDateTime,
            ErrorCode = status == "Failed" ? (reasonCode.Length is > 0 and <= 12 && reasonCode.All(char.IsAsciiDigit) ? "msg91_" + reasonCode : "provider_delivery_failed") : null };
    }
    public static void Apply(WhatsAppTaskNotification item, WhatsAppDeliveryEvent report)
    {
        // Delivery/read are stronger evidence than a delayed failed/sent callback.
        var current = item.DeliveryStatus;
        if (current == "Read" || current == "Delivered" && report.Status != "Read" || current == "Failed" && report.Status == "Sent") return;
        if (item.DeliveryUpdatedAtUtc > report.OccurredAtUtc && report.Status is "Sent" or "Failed") return;
        item.DeliveryStatus = report.Status;
        item.DeliveryUpdatedAtUtc = report.OccurredAtUtc > item.DeliveryUpdatedAtUtc || !item.DeliveryUpdatedAtUtc.HasValue ? report.OccurredAtUtc : item.DeliveryUpdatedAtUtc;
        if (!string.IsNullOrEmpty(report.MetaMessageId)) item.MetaMessageId = report.MetaMessageId;
        item.DeliveryErrorCode = report.ErrorCode;
        if (report.Status is "Delivered" or "Read") item.DeliveredAtUtc ??= report.OccurredAtUtc;
        if (report.Status == "Read") item.ReadAtUtc ??= report.OccurredAtUtc;
    }
}
public interface IMsg91DeliveryInbox { Task ReceiveAsync(WhatsAppDeliveryEvent report, CancellationToken token); }
public sealed class Msg91DeliveryInbox(ApplicationDbContext db) : IMsg91DeliveryInbox
{
    public async Task ReceiveAsync(WhatsAppDeliveryEvent r, CancellationToken token)
    {
        // Idempotent even when two webhook retries reach different ERP instances simultaneously.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"WhatsAppDeliveryEvents\" (\"EventKey\", \"RequestId\", \"MetaMessageId\", \"RecipientNumberHash\", \"Status\", \"ErrorCode\", \"OccurredAtUtc\", \"ReceivedAtUtc\", \"Applied\") VALUES ({r.EventKey}, {r.RequestId}, {r.MetaMessageId}, {r.RecipientNumberHash}, {r.Status}, {r.ErrorCode}, {r.OccurredAtUtc}, {r.ReceivedAtUtc}, false) ON CONFLICT (\"EventKey\") DO NOTHING", token);
    }
    public async Task ReconcileAsync(CancellationToken token)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // Lock notification rows before applying receipts so concurrent replicas cannot regress status.
            var items = await db.WhatsAppTaskNotifications.FromSqlRaw("SELECT n.* FROM \"WhatsAppTaskNotifications\" n WHERE EXISTS (SELECT 1 FROM \"WhatsAppDeliveryEvents\" e WHERE NOT e.\"Applied\" AND e.\"RequestId\" = n.\"ProviderMessageId\" AND e.\"RecipientNumberHash\" = n.\"RecipientNumberHash\") ORDER BY n.\"Id\" LIMIT 100 FOR UPDATE OF n SKIP LOCKED").ToListAsync(token);
            foreach (var item in items)
            {
                var events = await db.WhatsAppDeliveryEvents.Where(e => !e.Applied && e.RequestId == item.ProviderMessageId && e.RecipientNumberHash == item.RecipientNumberHash).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).ToListAsync(token);
                foreach (var report in events) { Msg91Delivery.Apply(item, report); report.Applied = true; }
            }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        });
    }
}
