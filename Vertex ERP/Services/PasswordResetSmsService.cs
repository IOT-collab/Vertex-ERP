using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VertexERP.Services;

public interface IPasswordResetSmsService
{
    bool IsConfigured { get; }
    Task SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default);
}

public sealed class PasswordResetSmsService(HttpClient client, IConfiguration configuration) : IPasswordResetSmsService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Sms:Infobip:ApiKey"]);

    public async Task SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Infobip SMS is not configured.");
        if (!Regex.IsMatch(phoneNumber, @"^[0-9]{10}$") || !Regex.IsMatch(otp, @"^[0-9]{6}$"))
            throw new ArgumentException("Invalid mobile number or OTP.");
        var baseUrl = configuration["Sms:Infobip:BaseUrl"] ?? "https://api.infobip.com";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" ||
            !endpoint.Host.EndsWith(".infobip.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new InvalidOperationException("An HTTPS Infobip API URL is required.");
        var template = configuration["Sms:Infobip:MessageTemplate"] ??
            "Your Vertex ERP password reset OTP is {otp}. Valid for 5 minutes. Do not share this code.";
        if (!template.Contains("{otp}", StringComparison.Ordinal))
            throw new InvalidOperationException("SMS template must contain {otp}.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/sms/3/messages"));
        request.Headers.Authorization = new AuthenticationHeaderValue("App", configuration["Sms:Infobip:ApiKey"]);
        request.Content = JsonContent.Create(new
        {
            messages = new[] { new {
                sender = configuration["Sms:Infobip:Sender"] ?? "ServiceSMS",
                destinations = new[] { new { to = "91" + phoneNumber } },
                content = new { text = template.Replace("{otp}", otp, StringComparison.Ordinal) }
            } }
        });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("messages", out var messages) || messages.GetArrayLength() != 1 ||
            !messages[0].TryGetProperty("status", out var status) || !status.TryGetProperty("groupId", out var group) ||
            !group.TryGetInt32(out var groupId) || (groupId != 1 && groupId != 3))
            throw new InvalidOperationException("Infobip did not accept the SMS.");
    }
}
