using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ForgotPasswordViewModel
{
    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Employee ID / Username")]
    public string Username { get; set; } = string.Empty;

    public bool MethodsAvailable { get; set; }
    public bool HasSms { get; set; }
    public bool HasEmail { get; set; }
    public string? MaskedPhone { get; set; }
    public string? MaskedEmail { get; set; }
    public string? StatusMessage { get; set; }
}

public sealed class BeginEmailOtpViewModel
{
    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the six-digit OTP.")]
    public string Otp { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 10)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
