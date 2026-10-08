using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class PasswordResetToken
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    [Required, MaxLength(128)] public string OtpHash { get; set; } = string.Empty;
    [Required, MaxLength(12)] public string VerificationMethod { get; set; } = "Email";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? SmsSentAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
