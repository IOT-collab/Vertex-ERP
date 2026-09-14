using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ForgotPasswordViewModel
{
    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Enter your registered 10-digit mobile number."), Display(Name = "Registered mobile number")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class VerifyResetOtpViewModel
{
    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Enter your registered 10-digit mobile number.")] public string PhoneNumber { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the six-digit OTP.")]
    [Display(Name = "Verification code")] public string Otp { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Enter your registered 10-digit mobile number.")] public string PhoneNumber { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 10)]
    [Display(Name = "New password")] public string NewPassword { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = string.Empty;
}
