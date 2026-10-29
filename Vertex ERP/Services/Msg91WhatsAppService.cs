using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VertexERP.Services;

public sealed class Msg91WhatsAppOptions
{
    public const string Section = "Msg91WhatsApp";
    public const string DefaultApiUrl = "https://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/";
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
    public string ApiUrl { get; set; } = DefaultApiUrl;
    public string IntegratedNumber { get; set; } = "919217614338";
    public string TemplateName { get; set; } = "vertex_task_assigned";
    public string LanguageCode { get; set; } = "en";
    public string Namespace { get; set; } = "cdc730df_d16e_48b9_94b9_a057d00e67a1";
    public string[] ParameterOrder { get; set; } = [];

    // These reason codes are safe for logs and health diagnostics; no credential or
    // employee data is included.
    public IReadOnlyList<string> ReadinessIssues
    {
        get
        {
            var issues = new List<string>();
            if (!SendingEnabled) issues.Add("sending_disabled");
            if (string.IsNullOrWhiteSpace(AuthKey) || AuthKey.Contains('\r') || AuthKey.Contains('\n')) issues.Add("auth_key_missing_or_invalid");
            if (!IsValidApiUrl(ApiUrl)) issues.Add("api_url_invalid");
            if (IntegratedNumber is null || !System.Text.RegularExpressions.Regex.IsMatch(IntegratedNumber, @"^[1-9][0-9]{10,14}$")) issues.Add("integrated_number_invalid");
            if (string.IsNullOrWhiteSpace(TemplateName) || !System.Text.RegularExpressions.Regex.IsMatch(TemplateName, @"^[A-Za-z0-9_]{1,100}$")) issues.Add("template_name_missing_or_invalid");
            if (string.IsNullOrWhiteSpace(LanguageCode) || !System.Text.RegularExpressions.Regex.IsMatch(LanguageCode, @"^[a-z]{2,3}(?:[_-][A-Za-z0-9]{2,8})*$")) issues.Add("language_code_missing_or_invalid");
            if (string.IsNullOrWhiteSpace(Namespace)
                || !System.Text.RegularExpressions.Regex.IsMatch(Namespace, @"^[0-9a-fA-F]{8}_[0-9a-fA-F]{4}_[0-9a-fA-F]{4}_[0-9a-fA-F]{4}_[0-9a-fA-F]{12}$"))
                issues.Add("namespace_missing_or_invalid");
            if (!TemplateMappingVerified) issues.Add("template_mapping_unverified");
            if (string.IsNullOrWhiteSpace(ApprovedTemplateText)
                || !System.Text.RegularExpressions.Regex.Matches(ApprovedTemplateText, @"\{\{\s*(\d+)\s*\}\}")
                    .Select(m => m.Groups[1].Value).Distinct().Order().SequenceEqual(new[] { "1", "2", "3", "4" }))
                issues.Add("approved_template_variables_invalid");
            if (ParameterOrder is not { Length: 4 } || ParameterOrder.Distinct().Count() != 4
                || !ParameterOrder.SequenceEqual(new[] { "EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline" }))
                issues.Add("template_parameter_order_invalid");
            return issues;
        }
    }

    public bool Ready => ReadinessIssues.Count == 0;

    private static bool IsValidApiUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("api.msg91.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && uri.AbsolutePath == "/api/v5/whatsapp/whatsapp-outbound-message/bulk/"
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}

public sealed record TaskWhatsAppData(string EmployeeName, string Phone, string TaskTitle, string AssignedBy, DateOnly Deadline);
public sealed record Msg91Result(string Outcome, int? HttpStatus = null, string? ProviderId = null, string? ErrorCode = null,
    TimeSpan? RetryAfter = null, string? SanitizedResponse = null, DateTime? SubmittedAtUtc = null);
public interface IMsg91WhatsAppService { Task<Msg91Result> SendTaskAsync(TaskWhatsAppData data, CancellationToken cancellationToken); }

public sealed class Msg91WhatsAppService(HttpClient client, IOptions<Msg91WhatsAppOptions> options) : IMsg91WhatsAppService
{
    public const string Endpoint = Msg91WhatsAppOptions.DefaultApiUrl;
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
                name = config.TemplateName, language = new { code = config.LanguageCode, policy = "deterministic" },
                @namespace = config.Namespace, to_and_components = new[] { new { to = new[] { phone }, components } } } } };
    }
    public async Task<Msg91Result> SendTaskAsync(TaskWhatsAppData data, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Ready) return new("Disabled", ErrorCode: "configuration_not_verified");
        var phone = NormalizePhone(data.Phone);
        if (phone == null) return new("Failed", ErrorCode: "invalid_employee_phone");
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl);
        request.Headers.Add("authkey", config.AuthKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(BuildPayload(data, config, phone));
        var submittedAtUtc = DateTime.UtcNow;
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
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
                        if (root.Value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String)
                        {
                            var candidate = item.GetString();
                            if (candidate is not null && System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^[A-Za-z0-9_-]{1,200}$")) id = candidate;
                            break;
                        }
                var sanitizedResponse = SanitizeProviderResponse(root);
                Msg91Result Result(string outcome, string? errorCode = null) => new(outcome, status, id, errorCode, retry, sanitizedResponse, submittedAtUtc);
                // Request IDs are the bulk endpoint's acceptance identifiers, not delivery receipts.
                if (response.IsSuccessStatusCode && !rejected && !string.IsNullOrWhiteSpace(id))
                    return Result("Accepted");
                if (status == 429 || (status >= 500 && rejected)) return Result("Retry", "provider_rejected_temporary");
                if (status >= 500 || response.IsSuccessStatusCode && !rejected) return Result("NeedsReview", "acceptance_unknown");
                return Result("Failed", "provider_rejected");
            }
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.NameResolutionError)
        { return new("Retry", ErrorCode: "dns_failure", SubmittedAtUtc: submittedAtUtc); }
        catch (HttpRequestException) { return new("NeedsReview", ErrorCode: "network_outcome_unknown", SubmittedAtUtc: submittedAtUtc); }
        catch (OperationCanceledException) { return new("NeedsReview", ErrorCode: "timeout_or_cancel_outcome_unknown", SubmittedAtUtc: submittedAtUtc); }
    }

    private static string? SanitizeProviderResponse(JsonElement? root)
    {
        if (!root.HasValue || root.Value.ValueKind != JsonValueKind.Object) return null;
        var safe = new Dictionary<string, object?>();
        if (root.Value.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            && status.GetString() is string statusText && new[] { "success", "error", "failed", "accepted", "rejected", "queued" }.Contains(statusText, StringComparer.OrdinalIgnoreCase))
            safe["status"] = statusText;
        if (root.Value.TryGetProperty("hasError", out var hasError) && hasError.ValueKind is JsonValueKind.True or JsonValueKind.False)
            safe["hasError"] = hasError.GetBoolean();
        foreach (var key in new[] { "message_id", "request_id" })
            if (root.Value.TryGetProperty(key, out var providerId) && providerId.ValueKind == JsonValueKind.String
                && providerId.GetString() is string id && System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9_-]{1,200}$"))
            { safe["provider_id"] = id; break; }
        return System.Text.Json.JsonSerializer.Serialize(safe);
    }
}
