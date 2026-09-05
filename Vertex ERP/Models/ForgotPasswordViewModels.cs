using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress, Display(Name = "Registered email address")]
    public string Email { get; set; } = string.Empty;
}

public sealed class VerifyResetOtpViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the six-digit OTP.")]
    [Display(Name = "Verification code")] public string Otp { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 10)]
    [Display(Name = "New password")] public string NewPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = string.Empty;
}
