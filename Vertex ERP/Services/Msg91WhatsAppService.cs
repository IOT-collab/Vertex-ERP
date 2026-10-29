using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VertexERP.Services;

public sealed class Msg91WhatsAppOptions
{
    public const string Section = "Msg91WhatsApp";
    public bool SendingEnabled { get; set; } // Requires explicit operator approval; defaults to off.
    public const string ConfirmedTemplateBody = """
Dear {{1}},

A new task has been assigned to you in VertexERP.

Task: {{2}}
Assigned By: {{3}}
Deadline: {{4}}

Please log in to VertexERP to review and complete your assigned task within the deadline.

Regards,
VertexERP Team
""";
    public bool TemplateMappingVerified { get; set; } = true;
    public string ApprovedTemplateText { get; set; } = ConfirmedTemplateBody;
    public bool WebhooksEnabled { get; set; }
    public string WebhookToken { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string IntegratedNumber { get; set; } = "919217614338";
    public string TemplateName { get; set; } = "vertex_task_assigned";
    public string Language { get; set; } = "en";
    public string Namespace { get; set; } = "cdc730df_d16e_48b9_94b9_a057d00e67a1";
    public string[] ParameterOrder { get; set; } = ["EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline"];
    public bool Ready => SendingEnabled && TemplateMappingVerified && !string.IsNullOrWhiteSpace(AuthKey)
        && !AuthKey.Contains('\r') && !AuthKey.Contains('\n') && !string.IsNullOrWhiteSpace(ApprovedTemplateText)
        && System.Text.RegularExpressions.Regex.Matches(ApprovedTemplateText, @"\{\{\s*(\d+)\s*\}\}").Select(m => m.Groups[1].Value).Distinct().Order().SequenceEqual(new[] { "1", "2", "3", "4" })
        && ParameterOrder is { Length: 4 } && ParameterOrder.Distinct().Count() == 4
        && ParameterOrder.All(x => new[] { "EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline" }.Contains(x));
}

public sealed record TaskWhatsAppData(string EmployeeName, string Phone, string TaskTitle, string AssignedBy, DateOnly Deadline);
public sealed record Msg91Result(string Outcome, int? HttpStatus = null, string? ProviderId = null, string? ErrorCode = null, TimeSpan? RetryAfter = null);
public interface IMsg91WhatsAppService { Task<Msg91Result> SendTaskAsync(TaskWhatsAppData data, CancellationToken cancellationToken); }

public sealed class Msg91WhatsAppService(HttpClient client, IOptions<Msg91WhatsAppOptions> options) : IMsg91WhatsAppService
{
    public const string Endpoint = "https://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/";
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Trim().Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '(' && c != ')').ToArray());
        if (text.StartsWith('+')) text = text[1..];
        if (text.StartsWith("00")) text = text[2..];
        if (text.Length == 11 && text.StartsWith('0')) text = text[1..];
        if (text.Length == 10) text = "91" + text;
        return text.Length is >= 11 and <= 15 && text[0] != '0' && text.All(c => c is >= '0' and <= '9') ? text : null;
    }
    public static object BuildPayload(TaskWhatsAppData data, Msg91WhatsAppOptions config, string phone)
    {
        var values = new Dictionary<string, string> { ["EmployeeName"] = data.EmployeeName, ["TaskTitle"] = data.TaskTitle,
            ["AssignedBy"] = data.AssignedBy, ["TaskDeadline"] = data.Deadline.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) };
        var components = config.ParameterOrder.Select((key, index) => new { key = "body_" + (index + 1), value = new { type = "text", value = values[key] } })
            .ToDictionary(x => x.key, x => x.value);
        return new { integrated_number = config.IntegratedNumber, content_type = "template",
            payload = new { messaging_product = "whatsapp", type = "template", template = new {
                name = config.TemplateName, language = new { code = config.Language, policy = "deterministic" },
                @namespace = config.Namespace, to_and_components = new[] { new { to = new[] { phone }, components } } } } };
    }
    public async Task<Msg91Result> SendTaskAsync(TaskWhatsAppData data, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Ready) return new("Disabled", ErrorCode: "configuration_not_verified");
        var phone = NormalizePhone(data.Phone);
        if (phone == null) return new("Failed", ErrorCode: "invalid_employee_phone");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("authkey", config.AuthKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(BuildPayload(data, config, phone));
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            // Never log a response body: providers may echo credentials or employee details.
            JsonDocument? json = null;
            try { json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); } catch (JsonException) { }
            using (json)
            {
                var root = json?.RootElement;
                bool rejected = root.HasValue && root.Value.ValueKind == JsonValueKind.Object &&
                    ((root.Value.TryGetProperty("hasError", out var error) && error.ValueKind == JsonValueKind.True)
                    || (root.Value.TryGetProperty("status", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() is "error" or "failed"));
                string? id = null;
                if (root.HasValue && root.Value.ValueKind == JsonValueKind.Object)
                    foreach (var key in new[] { "message_id", "request_id" })
                        if (root.Value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String) { id = item.GetString(); break; }
                // Request IDs are the bulk endpoint's acceptance identifiers, not delivery receipts.
                if (response.IsSuccessStatusCode && !rejected && !string.IsNullOrWhiteSpace(id))
                    return new("Accepted", status, id.Length <= 200 ? id : null);
                if (status == 429 || (status >= 500 && rejected)) return new("Retry", status, ErrorCode: "provider_rejected_temporary", RetryAfter: retry);
                if (status >= 500 || response.IsSuccessStatusCode && !rejected) return new("NeedsReview", status, ErrorCode: "acceptance_unknown");
                return new("Failed", status, ErrorCode: "provider_rejected");
            }
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.NameResolutionError)
        { return new("Retry", ErrorCode: "dns_failure"); }
        catch (HttpRequestException) { return new("NeedsReview", ErrorCode: "network_outcome_unknown"); }
        catch (OperationCanceledException) { return new("NeedsReview", ErrorCode: "timeout_or_cancel_outcome_unknown"); }
    }
}
