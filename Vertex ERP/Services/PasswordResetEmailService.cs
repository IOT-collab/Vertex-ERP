using System.Net;
using System.Net.Mail;

namespace VertexERP.Services;

public interface IPasswordResetEmailService { Task SendOtpAsync(string recipientEmail, string otp, CancellationToken cancellationToken = default); }

public sealed class PasswordResetEmailService : IPasswordResetEmailService
{
    private readonly IConfiguration _configuration;
    public PasswordResetEmailService(IConfiguration configuration) => _configuration = configuration;

    public async Task SendOtpAsync(string recipientEmail, string otp, CancellationToken cancellationToken = default)
    {
        var settings = _configuration.GetSection("Email:Smtp");
        var host = settings["Host"]; var sender = settings["FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender)) throw new InvalidOperationException("Email SMTP is not configured.");
        var port = int.TryParse(settings["Port"], out var configuredPort) ? configuredPort : 587;
        using var client = new SmtpClient(host, port) { EnableSsl = !bool.TryParse(settings["EnableSsl"], out var ssl) || ssl };
        var username = settings["Username"]; var password = settings["Password"];
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password)) client.Credentials = new NetworkCredential(username, password);
        using var message = new MailMessage(sender, recipientEmail) { Subject = "Vertex ERP password reset code", Body = $"Your Vertex ERP password reset code is: {otp}\n\nIt expires in 10 minutes. Do not share this code with anyone.", IsBodyHtml = false };
        await client.SendMailAsync(message, cancellationToken);
    }
}
