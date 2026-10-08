using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers
{
    [Authorize]
    public class MainController : Controller
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IAttendanceProcessingService _attendanceProcessingService;
        private readonly BankAccountProtectionService _bankProtection;
        private readonly IWebHostEnvironment _environment;
        private readonly IShipmentTrackingService _shipmentTrackingService;
        private readonly IPasswordResetSmsService _passwordResetSmsService;
        private static readonly ConcurrentDictionary<string, (DateTime StartedUtc, int Count)> RecoveryRequestWindows = new();

        public MainController(ApplicationDbContext dbContext, IAttendanceProcessingService attendanceProcessingService, BankAccountProtectionService bankProtection, IWebHostEnvironment environment, IShipmentTrackingService shipmentTrackingService, IPasswordResetSmsService passwordResetSmsService)
        {
            _dbContext = dbContext;
            _attendanceProcessingService = attendanceProcessingService;
            _bankProtection = bankProtection;
            _environment = environment;
            _shipmentTrackingService = shipmentTrackingService;
            _passwordResetSmsService = passwordResetSmsService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Start()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToRoleHome();
            }

            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe = false)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.ErrorMessage = "Username and password are required.";
                return View();
            }

            var normalizedUsername = DatabaseInitializer.NormalizeUsername(email);
            var normalizedLogin = email.Trim().ToLowerInvariant();
            // Password comparison must use the exact value created by HR. Trimming here can
            // silently change an otherwise valid credential.
            var normalizedPassword = password;

            // Always prefer the login username that HR saved for the employee. Looking up
            // username, employee ID and email in one query can select a different account
            // when one employee's username matches another employee's ID or email.
            var user = await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser => appUser.IsActive && appUser.NormalizedUsername == normalizedUsername);

            // Also check by Username (case-insensitive) in case of normalization issues
            user ??= await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser => appUser.IsActive && appUser.Username.ToUpper() == normalizedUsername);

            // Employee ID and corporate email remain supported as fallbacks, but only when
            // no account exists with the exact username entered on the login form.
            user ??= await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser =>
                    appUser.IsActive && appUser.Employee != null &&
                    (appUser.Employee.EmployeeCode.ToLower() == normalizedLogin ||
                     appUser.Employee.Email.ToLower() == normalizedLogin));

            if (user == null)
            {
                ViewBag.ErrorMessage = "Invalid username or password";
                return View();
            }

            if (!PasswordHashService.VerifyPassword(normalizedPassword, user.PasswordHash))
            {
                ViewBag.ErrorMessage = "Invalid username or password";
                return View();
            }

            var role = AccountRoleService.Normalize(user.Role);
            if (role == null || ((role == AccountRoleService.Manager || role == AccountRoleService.Employee) && !user.EmployeeId.HasValue))
            {
                ViewBag.ErrorMessage = "This login account is not correctly linked to an employee role. Please contact HR.";
                return View();
            }
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Role, role),
                new("username", user.Username)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
                });

            HttpContext.Session.SetString("email", user.Username);
            HttpContext.Session.SetString("username", user.Username);
            HttpContext.Session.SetString("role", role);
            HttpContext.Session.SetString("fullName", user.FullName);
            var welcomeGender = user.Employee?.Gender;
            HttpContext.Session.SetString("showWelcomePopup", "true");
            HttpContext.Session.SetString("welcomeGender", welcomeGender ?? string.Empty);
            return RedirectToRoleHome(role);
        }

        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            ClearPasswordRecoverySession();
            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!AllowRecoveryRequest("identify", 20, TimeSpan.FromMinutes(15)))
            {
                model.StatusMessage = "Please wait a few minutes before trying again.";
                return View(model);
            }

            ClearPasswordRecoverySession();
            var lookup = model.Username.Trim();
            var normalized = DatabaseInitializer.NormalizeUsername(lookup);
            var users = await _dbContext.AppUsers.Include(item => item.Employee)
                .Where(item => item.IsActive && item.Employee != null && item.Employee.IsActive &&
                    (item.NormalizedUsername == normalized || item.Username.ToUpper() == normalized || item.Employee.EmployeeCode.ToLower() == lookup.ToLower()))
                .Take(2).ToListAsync();
            var user = users.Count == 1 ? users[0] : null;
            if (user?.Employee is null)
            {
                ModelState.AddModelError(string.Empty, "We couldn't continue with those details. Check the Employee ID / Username or contact HR.");
                return View(model);
            }

            var phone = ToIndianE164(user.Employee.PhoneNumber);
            var email = user.Employee.Email?.Trim();
            model.HasSms = phone != null;
            model.HasEmail = HasDeliverableEmail(email);
            model.MaskedPhone = phone is null ? null : MaskPhone(phone);
            model.MaskedEmail = model.HasEmail ? MaskEmail(email!) : null;
            model.MethodsAvailable = model.HasSms || model.HasEmail;
            if (!model.MethodsAvailable)
            {
                ModelState.AddModelError(string.Empty, "We couldn't continue with those details. Contact HR for assistance.");
                return View(model);
            }

            HttpContext.Session.SetInt32("RecoveryUserId", user.Id);
            return View(model);
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> BeginEmailOtp()
        {
            var user = await GetRecoveryUserAsync();
            if (user?.Employee is null) return Json(new { ok = false, message = "Start again from the Employee ID / Username step." });
            if (!HasDeliverableEmail(user.Employee.Email))
                return BadRequest(new { ok = false, message = "Email verification is unavailable for this account. Choose SMS or contact HR." });
            if (!AllowRecoveryRequest($"otp:{user.Id}", 5, TimeSpan.FromHours(1)))
                return StatusCode(429, new { ok = false, message = "Please wait 60 seconds before requesting another code. The hourly limit is five codes." });
            var now = DateTime.UtcNow;
            var sentRecently = await _dbContext.PasswordResetTokens.AnyAsync(item => item.AppUserId == user.Id &&
                ((item.VerificationMethod == "Email" && item.AppUser!.Employee != null && !item.AppUser.Employee.Email.EndsWith("@import.vertex") && item.CreatedAtUtc > now.AddMinutes(-1)) || item.SmsSentAtUtc > now.AddMinutes(-1)));
            var recentCount = await _dbContext.PasswordResetTokens.CountAsync(item => item.AppUserId == user.Id &&
                ((item.VerificationMethod == "Email" && item.AppUser!.Employee != null && !item.AppUser.Employee.Email.EndsWith("@import.vertex") && item.CreatedAtUtc > now.AddHours(-1)) || item.SmsSentAtUtc > now.AddHours(-1)));
            if (sentRecently || recentCount >= 5)
                return StatusCode(429, new { ok = false, message = "Please wait 60 seconds before requesting another code. The hourly limit is five codes." });

            var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            await _dbContext.PasswordResetTokens.Where(item => item.AppUserId == user.Id && item.UsedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAtUtc, now));
            var token = new PasswordResetToken
            {
                AppUserId = user.Id,
                VerificationMethod = "Email",
                OtpHash = PasswordHashService.HashPassword(otp),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5)
            };
            _dbContext.PasswordResetTokens.Add(token);
            await _dbContext.SaveChangesAsync();
            try
            {
                await HttpContext.RequestServices.GetRequiredService<IPasswordResetEmailService>()
                    .SendOtpAsync(user.Employee.Email, otp, HttpContext.RequestAborted);
            }
            catch
            {
                token.UsedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return StatusCode(503, new { ok = false, message = "We couldn't send a code right now. Please try again later or use SMS." });
            }
            HttpContext.Session.SetInt32("RecoveryChallengeId", token.Id);
            HttpContext.Session.Remove("PasswordResetGrantTokenId");
            return Json(new { ok = true, message = $"A code was sent to {MaskEmail(user.Employee.Email)}. It expires in 5 minutes.", expiresInSeconds = 300, resendAfterSeconds = 60 });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmailOtp(BeginEmailOtpViewModel model)
        {
            var tokenId = HttpContext.Session.GetInt32("RecoveryChallengeId");
            if (!ModelState.IsValid || !tokenId.HasValue)
                return BadRequest(new { ok = false, message = "The code is invalid or has expired. Request a new code." });
            var token = await _dbContext.PasswordResetTokens.Include(item => item.AppUser)
                .FirstOrDefaultAsync(item => item.Id == tokenId && item.VerificationMethod == "Email" && item.UsedAtUtc == null && item.VerifiedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.FailedAttempts < 5 && item.AppUserId == HttpContext.Session.GetInt32("RecoveryUserId") && item.AppUser!.IsActive);
            if (token is null) return BadRequest(new { ok = false, message = "The code is invalid or has expired." });
            if (!PasswordHashService.VerifyPassword(model.Otp, token.OtpHash))
            {
                await _dbContext.PasswordResetTokens.Where(item => item.Id == token.Id && item.VerifiedAtUtc == null && item.UsedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.FailedAttempts < 5)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.FailedAttempts, item => item.FailedAttempts + 1));
                return BadRequest(new { ok = false, message = "The code is invalid or has expired." });
            }
            var verifiedAt = DateTime.UtcNow;
            var markedVerified = await _dbContext.PasswordResetTokens.Where(item => item.Id == token.Id && item.VerifiedAtUtc == null && item.UsedAtUtc == null && item.ExpiresAtUtc > verifiedAt && item.FailedAttempts < 5)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.VerifiedAtUtc, verifiedAt));
            if (markedVerified != 1) return BadRequest(new { ok = false, message = "The code is invalid or has expired." });
            HttpContext.Session.SetInt32("PasswordResetGrantTokenId", token.Id);
            HttpContext.Session.Remove("RecoveryChallengeId");
            HttpContext.Session.Remove("RecoveryUserId");
            return Json(new { ok = true, redirectUrl = Url.Action(nameof(ResetPassword)) });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> BeginSmsOtp()
        {
            var user = await GetRecoveryUserAsync();
            if (user?.Employee is null) return Json(new { ok = false, message = "Start again from the Employee ID / Username step." });
            var phone = ToIndianE164(user.Employee.PhoneNumber);
            if (phone is null) return BadRequest(new { ok = false, message = "SMS verification is unavailable for this account." });
            if (!AllowRecoveryRequest($"sms-start:{user.Id}", 20, TimeSpan.FromMinutes(15)))
                return StatusCode(429, new { ok = false, message = "Too many verification attempts. Please try again later." });
            var now = DateTime.UtcNow;
            var sentRecently = await _dbContext.PasswordResetTokens.AnyAsync(item => item.AppUserId == user.Id &&
                ((item.VerificationMethod == "Email" && item.AppUser!.Employee != null && !item.AppUser.Employee.Email.EndsWith("@import.vertex") && item.CreatedAtUtc > now.AddMinutes(-1)) || item.SmsSentAtUtc > now.AddMinutes(-1)));
            var recentCount = await _dbContext.PasswordResetTokens.CountAsync(item => item.AppUserId == user.Id &&
                ((item.VerificationMethod == "Email" && item.AppUser!.Employee != null && !item.AppUser.Employee.Email.EndsWith("@import.vertex") && item.CreatedAtUtc > now.AddHours(-1)) || item.SmsSentAtUtc > now.AddHours(-1)));
            if (sentRecently || recentCount >= 5)
                return StatusCode(429, new { ok = false, message = "Please wait 60 seconds before requesting another code. The hourly limit is five codes." });
            await _dbContext.PasswordResetTokens.Where(item => item.AppUserId == user.Id && item.UsedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAtUtc, now));
            var token = new PasswordResetToken { AppUserId = user.Id, VerificationMethod = "Sms", OtpHash = string.Empty, CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5) };
            _dbContext.PasswordResetTokens.Add(token);
            await _dbContext.SaveChangesAsync();
            HttpContext.Session.SetInt32("RecoveryChallengeId", token.Id);
            HttpContext.Session.Remove("PasswordResetGrantTokenId");
            return Json(new { ok = true, phoneNumber = phone, resendAfterSeconds = 60 });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkSmsSent()
        {
            var tokenId = HttpContext.Session.GetInt32("RecoveryChallengeId");
            var userId = HttpContext.Session.GetInt32("RecoveryUserId");
            if (!tokenId.HasValue || !userId.HasValue)
                return BadRequest(new { ok = false, message = "The verification session has expired. Start again." });
            var token = await _dbContext.PasswordResetTokens.FirstOrDefaultAsync(item => item.Id == tokenId && item.AppUserId == userId &&
                item.VerificationMethod == "Sms" && item.UsedAtUtc == null && item.VerifiedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow);
            if (token is null) return BadRequest(new { ok = false, message = "The verification session has expired. Start again." });
            if (token.SmsSentAtUtc.HasValue) return Json(new { ok = true });

            var now = DateTime.UtcNow;
            var recentCount = await _dbContext.PasswordResetTokens.CountAsync(item => item.AppUserId == userId &&
                ((item.VerificationMethod == "Email" && item.AppUser!.Employee != null && !item.AppUser.Employee.Email.EndsWith("@import.vertex") && item.CreatedAtUtc > now.AddHours(-1)) || item.SmsSentAtUtc > now.AddHours(-1)));
            if (recentCount >= 5)
            {
                token.UsedAtUtc = now;
                await _dbContext.SaveChangesAsync();
                return StatusCode(429, new { ok = false, message = "The hourly verification limit was reached. Please try again later." });
            }
            token.SmsSentAtUtc = now;
            await _dbContext.SaveChangesAsync();
            return Json(new { ok = true });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelSmsOtp()
        {
            var tokenId = HttpContext.Session.GetInt32("RecoveryChallengeId");
            if (tokenId.HasValue)
            {
                await _dbContext.PasswordResetTokens.Where(item => item.Id == tokenId && item.VerificationMethod == "Sms" && item.SmsSentAtUtc == null && item.UsedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAtUtc, DateTime.UtcNow));
                HttpContext.Session.Remove("RecoveryChallengeId");
            }
            return Json(new { ok = true });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifySmsOtp([FromBody] FirebaseTokenRequest request)
        {
            var tokenId = HttpContext.Session.GetInt32("RecoveryChallengeId");
            var userId = HttpContext.Session.GetInt32("RecoveryUserId");
            if (string.IsNullOrWhiteSpace(request.IdToken) || !tokenId.HasValue || !userId.HasValue)
                return BadRequest(new { ok = false, message = "The verification session has expired. Start again." });
            var token = await _dbContext.PasswordResetTokens.Include(item => item.AppUser).ThenInclude(item => item!.Employee)
                .FirstOrDefaultAsync(item => item.Id == tokenId && item.AppUserId == userId && item.VerificationMethod == "Sms" && item.SmsSentAtUtc != null && item.UsedAtUtc == null && item.VerifiedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AppUser!.IsActive && item.AppUser.Employee!.IsActive);
            if (token?.AppUser?.Employee is null) return BadRequest(new { ok = false, message = "The verification session has expired. Start again." });
            var identity = await HttpContext.RequestServices.GetRequiredService<IFirebaseIdTokenVerifier>()
                .VerifyAsync(request.IdToken, HttpContext.RequestAborted);
            var registeredPhone = ToIndianE164(token.AppUser.Employee.PhoneNumber);
            if (identity is null || registeredPhone is null || !string.Equals(ToIndianE164(identity.PhoneNumber), registeredPhone, StringComparison.Ordinal))
                return BadRequest(new { ok = false, message = "Phone verification failed. Check the code and try again." });
            token.VerifiedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            HttpContext.Session.SetInt32("PasswordResetGrantTokenId", token.Id);
            HttpContext.Session.Remove("RecoveryChallengeId");
            HttpContext.Session.Remove("RecoveryUserId");
            return Json(new { ok = true, redirectUrl = Url.Action(nameof(ResetPassword)) });
        }

        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword() => await HasValidPasswordResetGrantAsync()
            ? View(new ResetPasswordViewModel()) : RedirectToAction(nameof(ForgotPassword));

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var grantId = HttpContext.Session.GetInt32("PasswordResetGrantTokenId");
            if (!grantId.HasValue) return RedirectToAction(nameof(ForgotPassword));
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var grant = await _dbContext.PasswordResetTokens.Include(item => item.AppUser)
                .FirstOrDefaultAsync(item => item.Id == grantId && item.UsedAtUtc == null && item.VerifiedAtUtc != null && item.ExpiresAtUtc > now && item.AppUser!.IsActive);
            if (grant?.AppUser is null) return RedirectToAction(nameof(ForgotPassword));
            var consumed = await _dbContext.PasswordResetTokens.Where(item => item.Id == grant.Id && item.UsedAtUtc == null && item.VerifiedAtUtc != null && item.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAtUtc, now));
            if (consumed != 1) return RedirectToAction(nameof(ForgotPassword));
            var newHash = PasswordHashService.HashPassword(model.NewPassword);
            var updated = await _dbContext.AppUsers.Where(item => item.Id == grant.AppUserId && item.IsActive)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.PasswordHash, newHash)
                    .SetProperty(item => item.MustChangePassword, false)
                    .SetProperty(item => item.PasswordChangedAtUtc, now));
            if (updated != 1) return RedirectToAction(nameof(ForgotPassword));
            await _dbContext.PasswordResetTokens.Where(item => item.AppUserId == grant.AppUserId && item.UsedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAtUtc, now));
            _dbContext.AuditLogs.Add(AuditLogFactory.CreateEvent("AppUser", "Account password reset", $"Account #{grant.AppUserId} · Password reset completed", new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, grant.AppUserId.ToString()),
                new Claim(ClaimTypes.Name, grant.AppUser.Username),
                new Claim(ClaimTypes.Role, grant.AppUser.Role)
            }, "PasswordReset"))));
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            ClearPasswordRecoverySession();
            TempData["LoginMessage"] = "Password reset successfully. Please sign in.";
            return RedirectToAction(nameof(Login));
        }

        private async Task<bool> HasValidPasswordResetGrantAsync()
        {
            var grantId = HttpContext.Session.GetInt32("PasswordResetGrantTokenId");
            return grantId.HasValue && await _dbContext.PasswordResetTokens.AnyAsync(item => item.Id == grantId && item.UsedAtUtc == null && item.VerifiedAtUtc != null && item.ExpiresAtUtc > DateTime.UtcNow && item.AppUser!.IsActive);
        }

        private async Task<AppUser?> GetRecoveryUserAsync()
        {
            var userId = HttpContext.Session.GetInt32("RecoveryUserId");
            return userId.HasValue ? await _dbContext.AppUsers.Include(item => item.Employee)
                .FirstOrDefaultAsync(item => item.Id == userId && item.IsActive && item.Employee != null && item.Employee.IsActive) : null;
        }

        private void ClearPasswordRecoverySession()
        {
            HttpContext.Session.Remove("RecoveryUserId");
            HttpContext.Session.Remove("RecoveryChallengeId");
            HttpContext.Session.Remove("PasswordResetGrantTokenId");
            HttpContext.Session.Remove("ResetOtpTokenId");
            HttpContext.Session.Remove("PasswordResetHash");
        }

        private bool AllowRecoveryRequest(string key, int maximum, TimeSpan period)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
            var now = DateTime.UtcNow;
            var bucket = (ip + ":" + key, period);
            var item = RecoveryRequestWindows.AddOrUpdate(bucket.Item1,
                _ => (now, 1),
                (_, current) => now - current.StartedUtc >= period ? (now, 1) : (current.StartedUtc, current.Count + 1));
            if (RecoveryRequestWindows.Count > 10_000)
                foreach (var entry in RecoveryRequestWindows)
                    if (now - entry.Value.StartedUtc > TimeSpan.FromHours(2)) RecoveryRequestWindows.TryRemove(entry.Key, out _);
            return item.Count <= maximum;
        }

        private static string? ToIndianE164(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 10) return "+91" + digits;
            if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) return "+" + digits;
            if (digits.Length == 11 && digits.StartsWith("0", StringComparison.Ordinal)) return "+91" + digits[1..];
            if (digits.Length == 14 && digits.StartsWith("0091", StringComparison.Ordinal)) return "+" + digits[2..];
            return null;
        }

        private static string MaskPhone(string e164) => "******" + e164[^4..];

        private static string MaskEmail(string email)
        {
            var parts = email.Split('@', 2);
            if (parts.Length != 2) return "***";
            var local = parts[0];
            return (local.Length <= 3 ? local[..1] : local[..3]) + "****@" + parts[1];
        }

        private static bool HasDeliverableEmail(string? email) =>
            !string.IsNullOrWhiteSpace(email) &&
            !email.EndsWith("@import.vertex", StringComparison.OrdinalIgnoreCase) &&
            new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email);

        public sealed class FirebaseTokenRequest { public string IdToken { get; set; } = string.Empty; }

        [HttpGet]
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
            var user = await _dbContext.AppUsers.FirstOrDefaultAsync(item => item.Id == userId && item.IsActive);
            if (user == null) return Forbid();
            if (!PasswordHashService.VerifyPassword(model.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.CurrentPassword), "Current password is incorrect.");
                return View(model);
            }
            if (PasswordHashService.VerifyPassword(model.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.NewPassword), "New password must be different from the current password.");
                return View(model);
            }
            user.PasswordHash = PasswordHashService.HashPassword(model.NewPassword);
            user.MustChangePassword = false;
            await _dbContext.SaveChangesAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            TempData["LoginMessage"] = "Password changed successfully. Please sign in again.";
            return RedirectToAction(nameof(Login));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UserSettings()
        {
            ViewBag.Users = await _dbContext.AppUsers.AsNoTracking().OrderBy(user => user.Role).ThenBy(user => user.Username).ToListAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public IActionResult CreateUser(string username, string fullName, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password) || password.Length < 10)
            { TempData["UserSettingError"] = "Username, name and a password of at least 10 characters are required."; return RedirectToAction("UserSettings"); }
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(username);

            if (_dbContext.AppUsers.Any(user => user.NormalizedUsername == normalizedUsername))
            {
                TempData["UserSettingError"] = "This employee ID / username already exists.";
                return RedirectToAction("UserSettings");
            }

            _dbContext.AppUsers.Add(new AppUser
            {
                Username = username.Trim(),
                NormalizedUsername = normalizedUsername,
                PasswordHash = PasswordHashService.HashPassword(password),
                Role = AccountRoleService.Normalize(role) ?? AccountRoleService.Employee,
                FullName = fullName.Trim(),
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow
            });

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Login user created successfully.";
            return RedirectToAction("UserSettings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public IActionResult UpdateUser(int id, string username, string fullName, string role, bool isActive, string? newPassword)
        {
            var user = _dbContext.AppUsers.FirstOrDefault(appUser => appUser.Id == id);

            if (user == null)
            {
                TempData["UserSettingError"] = "Selected employee login was not found.";
                return RedirectToAction("UserSettings");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
            {
                TempData["UserSettingError"] = "Username and employee name are required.";
                return RedirectToAction("UserSettings");
            }

            username = username.Trim();
            if (username.Length > 50)
            {
                TempData["UserSettingError"] = "Username cannot exceed 50 characters.";
                return RedirectToAction("UserSettings");
            }

            var normalizedUsername = DatabaseInitializer.NormalizeUsername(username);
            if (_dbContext.AppUsers.Any(appUser => appUser.Id != id && appUser.NormalizedUsername == normalizedUsername))
            {
                TempData["UserSettingError"] = "This username is already assigned to another account.";
                return RedirectToAction("UserSettings");
            }

            if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 10)
            {
                TempData["UserSettingError"] = "The new password must contain at least 10 characters.";
                return RedirectToAction("UserSettings");
            }

            var allowedRoles = new[] { AccountRoleService.Employee, AccountRoleService.Manager, AccountRoleService.HR, AccountRoleService.Admin };
            var usernameChanged = !string.Equals(user.NormalizedUsername, normalizedUsername, StringComparison.Ordinal);
            var passwordChanged = !string.IsNullOrWhiteSpace(newPassword);
            if (usernameChanged)
            {
                user.Username = username;
                user.NormalizedUsername = normalizedUsername;
            }
            user.FullName = fullName.Trim();
            user.Role = allowedRoles.Contains(role) ? role : AccountRoleService.Employee;
            user.IsActive = isActive;
            if (passwordChanged)
            {
                user.PasswordHash = PasswordHashService.HashPassword(newPassword!);
                user.MustChangePassword = false;
            }

            if (user.EmployeeId.HasValue)
            {
                var employee = _dbContext.Employees.FirstOrDefault(item => item.Id == user.EmployeeId.Value);
                if (employee != null)
                {
                    employee.FullName = user.FullName;
                    var nameParts = user.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    employee.FirstName = nameParts.ElementAtOrDefault(0) ?? user.FullName;
                    employee.LastName = nameParts.ElementAtOrDefault(1);
                    employee.UpdatedDate = DateTime.UtcNow;
                }
            }

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = usernameChanged || passwordChanged
                ? "Employee credentials, role and access settings updated successfully."
                : "Employee role updated successfully. Existing username and password remain valid.";
            return RedirectToAction("UserSettings");
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Dashboard()
        {
            var today = FieldAttendanceClock.Now.Date;
            var tomorrow = today.AddDays(1);
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var employees = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .ToListAsync();
            var departments = await _dbContext.Departments.AsNoTracking()
                .Where(department => department.IsActive)
                .OrderBy(department => department.DepartmentName)
                .ToListAsync();
            var todayLogs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId != null && log.PunchTime >= today && log.PunchTime < tomorrow)
                .ToListAsync();
            var weekLogs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId != null && log.PunchTime >= weekStart && log.PunchTime < tomorrow)
                .ToListAsync();
            var tasks = await _dbContext.WorkTasks.AsNoTracking().ToListAsync();
            var activeEmployeeIds = employees.Select(employee => employee.Id).ToHashSet();
            var presentIds = todayLogs.Where(log => log.EmployeeId.HasValue).Select(log => log.EmployeeId!.Value).Distinct().ToHashSet();
            var onLeaveIds = (await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved" && request.FromDate <= DateOnly.FromDateTime(today) && request.ToDate >= DateOnly.FromDateTime(today))
                .Select(request => request.EmployeeId).Distinct().ToListAsync()).ToHashSet();
            presentIds.IntersectWith(activeEmployeeIds);
            onLeaveIds.IntersectWith(activeEmployeeIds);
            var lateCount = todayLogs.Where(log => log.EmployeeId.HasValue && activeEmployeeIds.Contains(log.EmployeeId.Value))
                .GroupBy(log => log.EmployeeId!.Value)
                .Count(group => group.Min(log => log.PunchTime).TimeOfDay > new TimeSpan(10, 0, 0));
            var taskHealth = TaskHealthSummary.From(tasks, DateOnly.FromDateTime(today));

            var model = new DashboardViewModel
            {
                TotalWorkforce = employees.Count,
                ActiveWorkforce = employees.Count,
                PresentToday = presentIds.Count,
                LateToday = lateCount,
                OnLeaveToday = employees.Count(employee => onLeaveIds.Contains(employee.Id)),
                AbsentToday = AttendanceRules.IsWeeklyOff(DateOnly.FromDateTime(today)) ? 0 : employees.Count(employee => !presentIds.Contains(employee.Id) && !onLeaveIds.Contains(employee.Id)),
                OpenTasks = taskHealth.OpenTasks,
                OverdueTasks = taskHealth.OverdueTasks,
                CompletedTasks = taskHealth.CompletedTasks,
                TaskProgressPercentage = taskHealth.Progress,
                RecentEmployees = employees.OrderByDescending(employee => employee.CreatedDate).Take(8)
                    .Select(employee => new DashboardEmployeeRow(employee.Id, employee.EmployeeCode, employee.FullName, employee.Email, employee.Department, employee.Designation, employee.IsActive, employee.PhotoPath)).ToList(),
                Departments = departments.Select(department => new DashboardDepartmentMetric(
                    department.DepartmentName,
                    employees.Count(employee => employee.DepartmentId == department.Id))).ToList(),
                WeeklyAttendance = Enumerable.Range(0, 6).Select(offset => weekStart.AddDays(offset))
                    .Select(day => new DashboardDayMetric(day.ToString("ddd"), weekLogs.Where(log => log.PunchTime.Date == day.Date && log.EmployeeId.HasValue && activeEmployeeIds.Contains(log.EmployeeId.Value)).Select(log => log.EmployeeId).Distinct().Count())).ToList(),
                RecentActivity = Array.Empty<DashboardActivityItem>()
            };
            return View(model);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> DashboardAttendance(string status, CancellationToken cancellationToken)
        {
            var normalizedStatus = status?.Trim();
            if (normalizedStatus is not ("Present" or "Absent" or "Late" or "Leave"))
                return BadRequest();

            var today = FieldAttendanceClock.Now.Date;
            var tomorrow = today.AddDays(1);
            var todayDate = DateOnly.FromDateTime(today);
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId != null && log.PunchTime >= today && log.PunchTime < tomorrow)
                .OrderBy(log => log.PunchTime)
                .ToListAsync(cancellationToken);
            var firstPunchByEmployee = logs.GroupBy(log => log.EmployeeId!.Value)
                .ToDictionary(group => group.Key, group => group.Min(log => log.PunchTime));
            var approvedLeaveRequests = await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved" && request.FromDate <= todayDate && request.ToDate >= todayDate)
                .ToListAsync(cancellationToken);
            var employees = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .OrderBy(employee => employee.FirstName).ThenBy(employee => employee.LastName)
                .ToListAsync(cancellationToken);
            var activeEmployeeIds = employees.Select(employee => employee.Id).ToHashSet();
            var leaveEmployeeIds = approvedLeaveRequests.Select(request => request.EmployeeId).Where(activeEmployeeIds.Contains).ToHashSet();
            var presentEmployeeIds = firstPunchByEmployee.Keys.Where(activeEmployeeIds.Contains).ToHashSet();

            IEnumerable<Employee> filteredEmployees = normalizedStatus switch
            {
                "Present" => employees.Where(employee => presentEmployeeIds.Contains(employee.Id)),
                "Absent" => AttendanceRules.IsWeeklyOff(todayDate)
                    ? Enumerable.Empty<Employee>()
                    : employees.Where(employee => employee.IsActive && !presentEmployeeIds.Contains(employee.Id) && !leaveEmployeeIds.Contains(employee.Id)),
                "Late" => employees.Where(employee => firstPunchByEmployee.TryGetValue(employee.Id, out var firstPunch)
                    && firstPunch.TimeOfDay > new TimeSpan(10, 0, 0)),
                "Leave" => employees.Where(employee => employee.IsActive && leaveEmployeeIds.Contains(employee.Id)),
                _ => Enumerable.Empty<Employee>()
            };

            var leaveTypes = approvedLeaveRequests.GroupBy(request => request.EmployeeId)
                .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(request => request.LeaveType).Distinct()));
            var rows = filteredEmployees.Select(employee =>
            {
                DateTime? firstPunch = firstPunchByEmployee.TryGetValue(employee.Id, out var punch) ? punch : null;
                return new DashboardAttendanceEmployeeRow(
                    employee.Id,
                    employee.EmployeeCode,
                    employee.FullName,
                    employee.Department,
                    employee.Designation,
                    firstPunch,
                    leaveTypes.GetValueOrDefault(employee.Id));
            }).ToList();

            return View(new DashboardAttendanceListViewModel { Status = normalizedStatus, Date = todayDate, Employees = rows });
        }

        [HttpGet]
        [Authorize(Roles = "Admin,HR")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> TaskHealth(CancellationToken cancellationToken)
        {
            var tasks = await _dbContext.WorkTasks.AsNoTracking()
                .Select(task => new WorkTask { Status = task.Status, DueDate = task.DueDate })
                .ToListAsync(cancellationToken);
            return Json(TaskHealthSummary.From(tasks, DateOnly.FromDateTime(DateTime.Today)));
        }

        [Authorize(Roles = "Employee,User")]
        public IActionResult EmployeeHome()
        {
            return RedirectToAction(nameof(Employees));
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeTasks()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var tasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Manager)
                .Where(task => task.AssigneeId == employee.Id).OrderBy(task => task.DueDate).ToListAsync();
            return View(new EmployeeTasksViewModel { Employee = employee, Tasks = tasks });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> UpdateMyTaskStatus(int id, string status)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var normalizedStatus = status switch { "In Progress" => "In Progress", "Completed" => "Completed", _ => "To Do" };
            var task = await _dbContext.WorkTasks.FirstOrDefaultAsync(x => x.Id == id && x.AssigneeId == employee.Id);
            if (task == null) return NotFound();
            task.Status = normalizedStatus;
            task.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            TempData["TaskMessage"] = $"{task.Title} status updated to {normalizedStatus}.";
            return RedirectToAction(nameof(EmployeeTasks));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> DeleteMyTask(int id)
        {
            await Task.CompletedTask;
            return Forbid();
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeAttendance()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var now = FieldAttendanceClock.Now.Date;
            var start = new DateTime(now.Year, now.Month, 1);
            var end = now.AddDays(1);
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= start && log.PunchTime < end)
                .OrderBy(log => log.PunchTime)
                .Select(log => new { log.PunchTime, log.PunchState, log.VerificationMode, log.BiometricDevice.CommunicationMode })
                .ToListAsync();
            var lastDay = end.AddDays(-1) < now ? end.AddDays(-1) : now;
            var days = new List<EmployeeAttendanceDay>();
            for (var date = start; date <= lastDay; date = date.AddDays(1))
            {
                var punches = logs.Where(log => log.PunchTime.Date == date.Date).ToList();
                var dateOnly = DateOnly.FromDateTime(date);
                var paired = AttendanceRules.PairPunches(punches.Select(punch => (punch.PunchTime, punch.PunchState)));
                var source = punches.Count == 0
                    ? "--"
                    : punches.Any(punch => string.Equals(punch.CommunicationMode, AttendanceRules.FieldCommunicationMode, StringComparison.OrdinalIgnoreCase))
                        ? "Field Attendance"
                        : punches.All(punch => string.Equals(punch.VerificationMode, "Manual Approved", StringComparison.OrdinalIgnoreCase))
                            ? "Manual Approved"
                            : "Biometric / Thumb";
                var attendanceStatus = paired.CheckIn.HasValue
                    ? AttendanceRules.IsLateArrival(paired.CheckIn.Value) ? "Late" : "Present"
                    : "Absent";
                days.Add(new EmployeeAttendanceDay(dateOnly, paired.CheckIn, paired.CheckOut, attendanceStatus, source));
            }
            return View(new EmployeeAttendanceViewModel { Employee = employee, StartDate = DateOnly.FromDateTime(start), EndDate = DateOnly.FromDateTime(now), Days = days.OrderByDescending(day => day.Date).ToList() });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeAssets()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var assets = await _dbContext.EmployeeAssets.AsNoTracking()
                .Where(asset => asset.EmployeeId == employee.Id)
                .OrderByDescending(asset => asset.IssueDate).ThenByDescending(asset => asset.Id).ToListAsync();
            return View(new EmployeeAssetsViewModel { Employee = employee, Assets = assets });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> RespondToAsset(int id, string decision)
        {
            if (decision != "Accept" && decision != "Decline") return BadRequest();
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return Forbid();
            var status = decision == "Accept" ? "Issued" : "Declined";
            // A conditional update prevents duplicate or competing responses and enforces ownership.
            var updated = await _dbContext.EmployeeAssets
                .Where(asset => asset.Id == id && asset.EmployeeId == employee.Id && asset.Status == "Pending")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(asset => asset.Status, status)
                    .SetProperty(asset => asset.RespondedAtUtc, DateTime.UtcNow));
            TempData["AssetMessage"] = updated == 0
                ? "This request is no longer pending or is unavailable."
                : decision == "Accept" ? "Asset accepted and issued to you." : "Asset declined. It has not been issued to you.";
            return RedirectToAction(nameof(EmployeeAssets));
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> FieldAttendance()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var today = FieldAttendanceClock.Now.Date;
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode == "Field")
                .OrderBy(log => log.PunchTime)
                .ToListAsync();
            var checkIn = logs.FirstOrDefault(log => log.PunchState == "Check In");
            var checkOut = logs.LastOrDefault(log => log.PunchState == "Check Out");
            return View("FieldAttendanceLive", new FieldAttendanceViewModel
            {
                HasCheckedIn = checkIn != null,
                CheckInSiteName = checkIn?.FieldSiteName,
                CheckOutSiteName = checkOut?.FieldSiteName,
                HasCheckedOut = checkOut != null,
                CheckInTime = checkIn?.PunchTime,
                CheckOutTime = checkOut?.PunchTime,
                CheckInLatitude = checkIn?.Latitude,
                CheckInLongitude = checkIn?.Longitude,
                CheckInAccuracyMetres = checkIn?.AccuracyMetres,
                CheckOutLatitude = checkOut?.Latitude,
                CheckOutLongitude = checkOut?.Longitude,
                CheckOutAccuracyMetres = checkOut?.AccuracyMetres
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> SubmitFieldAttendance([FromBody] FieldAttendanceRequest request)
        {
            if (request == null) return BadRequest(new { message = "Attendance details are required." });
            if (string.IsNullOrWhiteSpace(request.SiteName) || request.SiteName.Trim().Length > 160)
                return BadRequest(new { message = "Enter the site or customer name (up to 160 characters)." });
            if (request.Action is not ("Check In" or "Check Out"))
                return BadRequest(new { message = "Choose Check In or Check Out." });
            if (!request.Latitude.HasValue || !request.Longitude.HasValue || !request.AccuracyMetres.HasValue)
                return BadRequest(new { message = "Your current GPS location is required to mark attendance." });
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            if (request.Latitude.Value == 0 && request.Longitude.Value == 0)
                return BadRequest(new { message = "A valid GPS position is required. Turn on precise location and try again." });
            if (request.AccuracyMetres.Value > 50)
                return BadRequest(new { message = $"GPS accuracy is only ±{request.AccuracyMetres.Value:F0} metres. Attendance requires accuracy within 50 metres. Move outdoors or near a window and try again." });
            if (!request.CapturedAtUtc.HasValue || Math.Abs((DateTimeOffset.UtcNow - request.CapturedAtUtc.Value).TotalMinutes) > 2)
                return BadRequest(new { message = "Your GPS reading is no longer fresh. Capture the current location again." });

            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return Unauthorized();
            var receivedAtUtc = DateTimeOffset.UtcNow;
            var now = FieldAttendanceClock.IndiaTime(receivedAtUtc);
            var today = now.Date;
            var existingActions = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode == "Field")
                .Select(log => log.PunchState)
                .ToListAsync();
            if (existingActions.Contains(request.Action))
                return Conflict(new { message = $"You have already completed {request.Action.ToLowerInvariant()} today." });
            if (request.Action == "Check Out" && !existingActions.Contains("Check In"))
                return BadRequest(new { message = "Please check in before checking out." });

            var device = await _dbContext.BiometricDevices.FirstOrDefaultAsync(item => item.SerialNumber == "FIELD-ATTENDANCE");
            if (device == null)
            {
                device = new BiometricDevice { Name = "ERP Field Attendance", SerialNumber = "FIELD-ATTENDANCE", Model = "Mobile GPS", CommunicationMode = "Field", Notes = "GPS-based employee field attendance." };
                _dbContext.BiometricDevices.Add(device);
                await _dbContext.SaveChangesAsync();
            }

            var selfiePath = await SaveFieldAttendanceSelfieAsync(request.SelfieDataUrl, employee.Id, request.Action);
            var rawPayload = $"FIELD|Action:{request.Action}|Latitude:{request.Latitude:F6}|Longitude:{request.Longitude:F6}|Accuracy:{request.AccuracyMetres:F1}m|Selfie:{selfiePath ?? "Not captured"}";
            _dbContext.AttendanceLogs.Add(new AttendanceLog
            {
                BiometricDeviceId = device.Id,
                EmployeeId = employee.Id,
                DeviceUserId = employee.EmployeeCode,
                PunchTime = now,
                PunchState = request.Action,
                VerificationMode = AttendanceRules.FieldVerificationMode,
                WorkCode = "Field Attendance",
                UniqueHash = Guid.NewGuid().ToString("N"),
                RawPayload = rawPayload,
                SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                AccuracyMetres = request.AccuracyMetres,
                FieldSiteName = request.SiteName.Trim(),
                LocationCapturedAtUtc = request.CapturedAtUtc.Value.ToUniversalTime(),
                ReceivedAtUtc = receivedAtUtc.UtcDateTime,
                SelfiePath = selfiePath
            });
            await _dbContext.SaveChangesAsync();
            return Ok(new
            {
                message = $"{request.Action} recorded with GPS at {now:dd MMM yyyy, hh:mm tt}.",
                time = now.ToString("dd MMM yyyy, hh:mm tt") + " IST",
                siteName = request.SiteName.Trim(),
                latitude = request.Latitude.Value.ToString("F6"),
                longitude = request.Longitude.Value.ToString("F6"),
                accuracyMetres = request.AccuracyMetres.Value.ToString("F0"),
                selfiePath
            });
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeLeaves(int? year = null)
        {
            var selectedYear = year ?? DateTime.Today.Year;
            if (selectedYear is < 2000 or > 2100) return BadRequest();
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var requests = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id).OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            var balances = await _dbContext.ManualLeaveBalances.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.Year == selectedYear).OrderBy(x => x.Category).ToListAsync();
            return View(new EmployeeLeaveViewModel { Employee = employee, Requests = requests, Balances = balances, Year = selectedYear });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR,Admin")]
        public IActionResult CompanyHoliday()
        {
            return View(new CompanyHolidayCalendar(DateTimeOffset.UtcNow));
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeProfile()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            ViewBag.BankDetail = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id);
            return View(employee);
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EditEmployeeProfile()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            return View(await PopulateEmployeeProfileOptionsAsync(new EmployeeProfileEditViewModel
            {
                EmployeeCode = employee.EmployeeCode, FullName = employee.FullName, Email = employee.Email, PhoneNumber = employee.PhoneNumber,
                DateOfBirth = employee.DateOfBirth, Gender = employee.Gender, MaritalStatus = employee.MaritalStatus, EmergencyContact = employee.EmergencyContact ?? string.Empty,
                DepartmentId = employee.DepartmentId, Designation = employee.Designation, ReportingManagerId = employee.ReportingManagerId, JoiningDate = employee.JoiningDate,
                EmploymentType = employee.EmploymentType, WorkLocation = employee.WorkLocation, Address = employee.Address, City = employee.City, State = employee.State, PinCode = employee.PinCode
            }, employee.Id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EditEmployeeProfile(EmployeeProfileEditViewModel model)
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return RedirectToAction(nameof(AccessDenied));
            var employee = await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == employeeId.Value);
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (!ModelState.IsValid)
            {
                model.EmployeeCode = employee.EmployeeCode;
                model.FullName = employee.FullName;
                model.Email = employee.Email;
                model.PhoneNumber = employee.PhoneNumber;
                model = await PopulateEmployeeProfileOptionsAsync(model, employee.Id);
                return View(model);
            }
            employee.DateOfBirth = model.DateOfBirth;
            employee.Gender = CleanProfileValue(model.Gender);
            employee.MaritalStatus = CleanProfileValue(model.MaritalStatus);
            employee.EmergencyContact = model.EmergencyContact.Trim();
            employee.WorkLocation = CleanProfileValue(model.WorkLocation);
            employee.Address = CleanProfileValue(model.Address);
            employee.City = CleanProfileValue(model.City);
            employee.State = CleanProfileValue(model.State);
            employee.PinCode = CleanProfileValue(model.PinCode);
            employee.UpdatedDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            TempData["ProfileMessage"] = "Profile updated successfully. Changes are visible to HR and Admin.";
            return RedirectToAction(nameof(EmployeeProfile));
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeNotifications()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var assignedTasks = await _dbContext.WorkTasks.AsNoTracking().Where(task => task.AssigneeId == employee.Id).ToListAsync();
            var ownLeaves = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id).ToListAsync();
            var dismissed = (await _dbContext.EmployeeNotificationDismissals.AsNoTracking().Where(x => x.EmployeeId == employee.Id).Select(x => new { x.SourceType, x.SourceId }).ToListAsync()).Select(x => $"{x.SourceType}:{x.SourceId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var taskItems = assignedTasks.Where(task => !dismissed.Contains($"Task:{task.Id}")).Select(task => new EmployeeNotificationItem(task.Id, "Task assigned: " + task.Title, "Status: " + task.Status + " · Due " + task.DueDate.ToString("dd MMM yyyy"), task.CreatedAtUtc, "Task"));
            var leaveItems = ownLeaves.Where(request => !dismissed.Contains($"Leave:{request.Id}")).Select(request => new EmployeeNotificationItem(request.Id, "Leave request " + request.Status, request.LeaveType + " · " + request.FromDate.ToString("dd MMM") + " - " + request.ToDate.ToString("dd MMM yyyy"), request.AppliedAtUtc, "Leave"));
            var ownTickets = await _dbContext.QueryTickets.AsNoTracking().Where(ticket => ticket.EmployeeId == employee.Id).ToListAsync();
            var ticketItems = ownTickets.Where(ticket => !dismissed.Contains($"Query:{ticket.Id}")).Select(ticket => new EmployeeNotificationItem(ticket.Id, "Query: " + ticket.Subject, "Status: " + ticket.Status + (string.IsNullOrWhiteSpace(ticket.Resolution) ? string.Empty : " · " + ticket.Resolution), ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc, "Query"));
            return View(new EmployeeNotificationsViewModel { Employee = employee, Items = taskItems.Concat(leaveItems).Concat(ticketItems).OrderByDescending(item => item.CreatedAt).ToList() });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> DeleteNotification(string type, int sourceId)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (type is not ("Task" or "Leave" or "Query") || sourceId <= 0) return BadRequest();
            var belongsToEmployee = type switch
            {
                "Task" => await _dbContext.WorkTasks.AnyAsync(x => x.Id == sourceId && x.AssigneeId == employee.Id),
                "Leave" => await _dbContext.LeaveRequests.AnyAsync(x => x.Id == sourceId && x.EmployeeId == employee.Id),
                _ => await _dbContext.QueryTickets.AnyAsync(x => x.Id == sourceId && x.EmployeeId == employee.Id)
            };
            if (!belongsToEmployee) return NotFound();
            if (!await _dbContext.EmployeeNotificationDismissals.AnyAsync(x => x.EmployeeId == employee.Id && x.SourceType == type && x.SourceId == sourceId))
                _dbContext.EmployeeNotificationDismissals.Add(new EmployeeNotificationDismissal { EmployeeId = employee.Id, SourceType = type, SourceId = sourceId });
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(EmployeeNotifications));
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeQueries()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Include(ticket => ticket.ResolvedByUser)
                .Where(ticket => ticket.EmployeeId == employee.Id).OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            return View(new EmployeeQueryViewModel { Employee = employee, Tickets = tickets });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> RaiseQuery(string subject, string category, string description)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
            {
                TempData["QueryError"] = "Subject and query details are required.";
                return RedirectToAction(nameof(EmployeeQueries));
            }
            _dbContext.QueryTickets.Add(new QueryTicket { EmployeeId = employee.Id, ReportingManagerId = employee.ReportingManagerId, Subject = subject.Trim(), Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(), Description = description.Trim() });
            await _dbContext.SaveChangesAsync();
            TempData["QueryMessage"] = "Your query was sent to your reporting manager and HR.";
            return RedirectToAction(nameof(EmployeeQueries));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> DeleteMyQuery(int id)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var ticket = await _dbContext.QueryTickets.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
            if (ticket == null) return NotFound();
            _dbContext.QueryTickets.Remove(ticket);
            await _dbContext.SaveChangesAsync();
            TempData["QueryMessage"] = "Query deleted successfully.";
            return RedirectToAction(nameof(EmployeeQueries));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> LocationTracking(DateOnly? date, int? employeeId)
        {
            var selectedDate = date ?? DateOnly.FromDateTime(FieldAttendanceClock.Now);
            var start = selectedDate.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            var employeesQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
            var hasCompanyWideAccess = User.IsInRole("Admin") || User.IsInRole("HR");
            var isManagerOnlyView = User.IsInRole("Manager") && !hasCompanyWideAccess;
            if (isManagerOnlyView)
            {
                var managerEmployeeId = await GetLoggedInEmployeeIdAsync();
                if (!managerEmployeeId.HasValue) return Forbid();
                // A manager can only query employees whose Reporting Manager is
                // directly assigned to this manager.
                // The employeeId query-string is applied after this server-side scope.
                employeesQuery = employeesQuery.Where(employee => employee.ReportingManagerId == managerEmployeeId.Value);
            }

            var employees = await employeesQuery
                .OrderBy(employee => employee.FullName)
                .Select(employee => new { employee.Id, employee.FullName, employee.EmployeeCode, employee.Department, employee.Designation })
                .ToListAsync();
            var visibleEmployeeIds = employees.Select(employee => employee.Id).ToList();
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue && visibleEmployeeIds.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= start && log.PunchTime < end
                    && log.BiometricDevice.CommunicationMode == "Field")
                .OrderBy(log => log.PunchTime).ToListAsync();
            var items = employees.Select(employee =>
            {
                var employeeLogs = logs.Where(log => log.EmployeeId == employee.Id).ToList();
                var checkIn = employeeLogs.FirstOrDefault(log => log.PunchState == "Check In");
                var checkOut = employeeLogs.LastOrDefault(log => log.PunchState == "Check Out");
                var last = checkOut ?? employeeLogs.LastOrDefault();
                return new SiteEmployeeLocationItem
                {
                    EmployeeId = employee.Id, EmployeeName = employee.FullName, EmployeeCode = employee.EmployeeCode,
                    Department = employee.Department, Designation = employee.Designation,
                    CheckInSiteName = checkIn?.FieldSiteName, CheckOutSiteName = checkOut?.FieldSiteName,
                    CheckInTime = checkIn?.PunchTime, CheckInLatitude = checkIn?.Latitude, CheckInLongitude = checkIn?.Longitude,
                    CheckInAccuracyMetres = checkIn?.AccuracyMetres, CheckInSelfiePath = checkIn?.SelfiePath,
                    CheckOutTime = checkOut?.PunchTime, CheckOutLatitude = checkOut?.Latitude, CheckOutLongitude = checkOut?.Longitude,
                    CheckOutAccuracyMetres = checkOut?.AccuracyMetres, CheckOutSelfiePath = checkOut?.SelfiePath,
                    LastLocationTime = last?.PunchTime, LastLatitude = last?.Latitude, LastLongitude = last?.Longitude
                };
            }).ToList();
            var selected = items.FirstOrDefault(item => item.EmployeeId == employeeId) ?? items.FirstOrDefault(item => item.CheckInTime.HasValue);
            return View("LocationTrackingLive", new SiteEmployeeLocationViewModel
            {
                Employees = items,
                SelectedDate = selectedDate,
                SelectedEmployee = selected,
                IsManagerView = isManagerOnlyView
            });
        }


        [Authorize(Roles = "Employee,User,Admin,HR,Manager")]
        public async Task<IActionResult> Employees(int? id)
        {
            if (User.IsInRole("Admin") || User.IsInRole("HR") || User.IsInRole("Manager"))
            {
                var employeesQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
                if (User.IsInRole("Manager"))
                {
                    var managerEmployeeId = await GetLoggedInEmployeeIdAsync();
                    var managerDepartmentId = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerEmployeeId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync();
                    employeesQuery = employeesQuery.Where(employee => managerDepartmentId.HasValue && employee.Id != managerEmployeeId && employee.DepartmentId == managerDepartmentId && !_dbContext.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"));
                }
                var employees = await employeesQuery.OrderBy(employee => employee.FullName).ToListAsync();
                var employeeIds = employees.Select(employee => employee.Id).ToList();
                var today = DateTime.Today;
                var presentIds = await _dbContext.AttendanceLogs.AsNoTracking()
                    .Where(log => log.EmployeeId.HasValue && employeeIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < today.AddDays(1))
                    .Select(log => log.EmployeeId!.Value).Distinct().ToListAsync();
                var onLeaveIds = await _dbContext.LeaveRequests.AsNoTracking()
                    .Where(request => employeeIds.Contains(request.EmployeeId) && request.Status == "Approved"
                        && request.FromDate <= DateOnly.FromDateTime(today) && request.ToDate >= DateOnly.FromDateTime(today))
                    .Select(request => request.EmployeeId).Distinct().ToListAsync();
                var tasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Assignee)
                    .Where(task => employeeIds.Contains(task.AssigneeId)).OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
                return View("EmployeeOverview", new WorkforceOverviewViewModel { Employees = employees, Tasks = tasks, PresentEmployeeIds = presentIds.ToHashSet(), OnLeaveEmployeeIds = onLeaveIds.ToHashSet(), IsWeeklyOff = AttendanceRules.IsWeeklyOff(DateOnly.FromDateTime(today)) });
            }

            Employee? employee;
            if (User.IsInRole("Employee") || User.IsInRole("User"))
            {
                var linkedEmployeeId = await GetLoggedInEmployeeIdAsync();
                employee = linkedEmployeeId.HasValue
                    ? await _dbContext.Employees.AsNoTracking()
                        .Include(item => item.ReportingManager)
                        .FirstOrDefaultAsync(item => item.Id == linkedEmployeeId.Value && item.IsActive)
                    : null;
            }
            else
            {
                employee = id.HasValue
                    ? await _dbContext.Employees.AsNoTracking()
                        .Include(item => item.ReportingManager)
                        .FirstOrDefaultAsync(item => item.Id == id.Value)
                    : await _dbContext.Employees.AsNoTracking()
                        .Include(item => item.ReportingManager)
                        .OrderByDescending(item => item.UpdatedDate ?? item.CreatedDate)
                        .FirstOrDefaultAsync();
            }

            if (employee == null) return View("EmployeeDashboard", new EmployeePortalViewModel());
            var employeeTasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Manager)
                .Where(task => task.AssigneeId == employee.Id).OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
            var employeeLeaves = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id)
                .OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            var todayStart = DateTime.Today;
            var punches = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId == employee.Id
                    && log.PunchTime >= todayStart && log.PunchTime < todayStart.AddDays(1)
                    && log.BiometricDevice.CommunicationMode != "Field")
                .OrderBy(log => log.PunchTime).Select(log => new { log.PunchTime, log.PunchState }).ToListAsync();
            var pairedPunches = AttendanceRules.PairPunches(punches.Select(punch => (punch.PunchTime, punch.PunchState)));
            return View("EmployeeDashboard", new EmployeePortalViewModel { Employee = employee, Tasks = employeeTasks, LeaveRequests = employeeLeaves, CheckIn = pairedPunches.CheckIn, CheckOut = pairedPunches.CheckOut });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> MyLiveAttendance(CancellationToken cancellationToken)
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return Forbid();

            var today = DateTime.Today;
            var punches = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employeeId.Value
                    && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode != "Field")
                .OrderBy(log => log.PunchTime)
                .Select(log => new { log.PunchTime, log.PunchState })
                .ToListAsync(cancellationToken);

            var pairedPunches = AttendanceRules.PairPunches(punches.Select(punch => (punch.PunchTime, punch.PunchState)));
            return Ok(new
            {
                checkIn = pairedPunches.CheckIn?.ToString("hh:mm tt"),
                checkOut = pairedPunches.CheckOut?.ToString("hh:mm tt"),
                status = pairedPunches.CheckIn.HasValue
                    ? AttendanceRules.IsLateArrival(pairedPunches.CheckIn.Value) ? "Late" : "Present"
                    : "Absent"
            });
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult EmpAddRequirement()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Attendence(string? searchQuery, string? department, DateOnly? filterDate, string? status, CancellationToken cancellationToken)
        {
            var date = filterDate ?? DateOnly.FromDateTime(DateTime.Today);
            return View(await _attendanceProcessingService.GetDailyAttendanceAsync(date, searchQuery, department, status, cancellationToken));
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> ExportAttendance(string? searchQuery, string? department, DateOnly? filterDate, string? status, string exportPeriod = "day", string? exportMonth = null, CancellationToken cancellationToken = default)
        {
            var date = filterDate ?? DateOnly.FromDateTime(DateTime.Today);
            var isMonthlyExport = string.Equals(exportPeriod, "month", StringComparison.OrdinalIgnoreCase);
            var dates = new List<DateOnly>();
            if (isMonthlyExport)
            {
                if (!DateOnly.TryParseExact($"{exportMonth}-01", "yyyy-MM-dd", out var monthStart)) return BadRequest("Select a valid month.");
                if (monthStart > DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Future month attendance cannot be exported.");
                var lastDay = monthStart.AddMonths(1).AddDays(-1);
                if (lastDay > DateOnly.FromDateTime(DateTime.Today)) lastDay = DateOnly.FromDateTime(DateTime.Today);
                for (var day = monthStart; day <= lastDay; day = day.AddDays(1)) dates.Add(day);
            }
            else
            {
                if (date > DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Future attendance cannot be exported.");
                dates.Add(date);
            }
            var csv = new StringBuilder();
            var exportRecords = new List<DailyAttendanceViewModel>();
            foreach (var exportDate in dates)
            {
                var attendance = await _attendanceProcessingService.GetDailyAttendanceAsync(exportDate,
                    isMonthlyExport ? null : searchQuery, isMonthlyExport ? null : department, isMonthlyExport ? null : status, cancellationToken);
                exportRecords.AddRange(attendance.Records.Where(item => item.EmployeeId > 0));
            }
            var monthlySummary = exportRecords.GroupBy(item => item.EmployeeId).ToDictionary(group => group.Key, group => new
            {
                Present = group.Count(item => item.CheckIn.HasValue),
                Absent = group.Count(item => item.Status == "Absent"),
                Late = group.Count(item => item.IsLate),
                Work = TimeSpan.FromTicks(group.Sum(item => item.WorkingHours.Ticks))
            });
            if (isMonthlyExport)
                return BuildMonthlyAttendanceWorkbook(exportRecords, dates, monthlySummary.ToDictionary(x => x.Key, x => (x.Value.Present, x.Value.Absent, x.Value.Late, x.Value.Work)));
            csv.AppendLine("Emp ID,Employee Name,Department,Date,Day,Check In,Check Out,Total Hours,Punch Count,Status,Remark,Late Arrival,Month Present Days,Month Absent Days,Month Late Days,Month Total Hours,Overtime,Site Check In - Time / Location / GPS,Site Check Out - Time / Location / GPS");
            foreach (var item in exportRecords.OrderBy(item => item.EmployeeName).ThenBy(item => item.EmpId).ThenBy(item => item.Date))
            {
                var summary = monthlySummary[item.EmployeeId];
                csv.AppendLine(string.Join(',', new[]
                {
                    Csv(item.EmpId), Csv(item.EmployeeName), Csv(item.Department), Csv(item.Date.ToString("dd-MMM-yyyy")), Csv(item.Date.DayOfWeek.ToString()),
                    Csv(item.CheckIn?.ToString("hh:mm tt") ?? string.Empty), Csv(item.CheckOut?.ToString("hh:mm tt") ?? string.Empty),
                    Csv(item.TotalHoursDisplay), item.PunchCount.ToString(), Csv(item.Status), Csv(item.Remark), Csv(item.IsLate ? "Yes" : "No"),
                    summary.Present.ToString(), summary.Absent.ToString(), summary.Late.ToString(), Csv($"{(int)summary.Work.TotalHours:D2}:{summary.Work.Minutes:D2}"),
                    Csv(item.OvertimeDisplay), Csv(item.SiteCheckIns), Csv(item.SiteCheckOuts)
                }));
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
            var periodLabel = dates.Count == 1 ? dates[0].ToString("yyyy-MM-dd") : dates[0].ToString("yyyy-MM");
            return File(bytes, "text/csv; charset=utf-8", $"Attendance-{periodLabel}.csv");
        }

        private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        private FileContentResult BuildMonthlyAttendanceWorkbook(List<DailyAttendanceViewModel> records, List<DateOnly> dates, Dictionary<int, (int Present, int Absent, int Late, TimeSpan Work)> summaries)
        {
            static string X(string? value) => System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
            var month = dates[0];
            var xml = new StringBuilder("<?xml version=\"1.0\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
            xml.Append("<Styles><Style ss:ID=\"Default\"><Alignment ss:Vertical=\"Center\"/><Font ss:FontName=\"Calibri\" ss:Size=\"10\"/></Style><Style ss:ID=\"Title\"><Alignment ss:Horizontal=\"Center\"/><Font ss:Bold=\"1\" ss:Size=\"16\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#1D4ED8\" ss:Pattern=\"Solid\"/></Style><Style ss:ID=\"Header\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#1E3A5F\" ss:Pattern=\"Solid\"/></Style><Style ss:ID=\"Text\"><Alignment ss:Vertical=\"Top\" ss:WrapText=\"1\"/><Borders><Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#DCE3EA\"/></Borders></Style><Style ss:ID=\"Present\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Interior ss:Color=\"#DCFCE7\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#166534\"/></Style><Style ss:ID=\"Late\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Interior ss:Color=\"#FEF3C7\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#92400E\"/></Style><Style ss:ID=\"Absent\"><Alignment ss:Horizontal=\"Center\"/><Interior ss:Color=\"#FEE2E2\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#991B1B\"/></Style></Styles>");
            xml.Append($"<Worksheet ss:Name=\"{X(month.ToString("MMM-yyyy"))}\"><Table><Column ss:Width=\"65\"/><Column ss:Width=\"145\"/><Column ss:Width=\"100\"/><Column ss:Width=\"55\" ss:Span=\"2\"/><Column ss:Width=\"70\" ss:Span=\"1\"/>");
            foreach (var _ in dates) xml.Append("<Column ss:Width=\"82\"/>");
            var columnCount = 8 + dates.Count;
            xml.Append($"<Row ss:Height=\"30\"><Cell ss:StyleID=\"Title\" ss:MergeAcross=\"{columnCount - 1}\"><Data ss:Type=\"String\">Monthly Attendance Pivot Report - {X(month.ToString("MMMM yyyy"))}</Data></Cell></Row>");
            xml.Append("<Row ss:StyleID=\"Header\"><Cell><Data ss:Type=\"String\">Emp ID</Data></Cell><Cell><Data ss:Type=\"String\">Employee Name</Data></Cell><Cell><Data ss:Type=\"String\">Department</Data></Cell><Cell><Data ss:Type=\"String\">Present</Data></Cell><Cell><Data ss:Type=\"String\">Absent</Data></Cell><Cell><Data ss:Type=\"String\">Late</Data></Cell><Cell><Data ss:Type=\"String\">Total Hours</Data></Cell>");
            xml.Append("<Cell><Data ss:Type=\"String\">Overtime</Data></Cell>");
            foreach (var day in dates) xml.Append($"<Cell><Data ss:Type=\"String\">{day:dd MMM}\n{day:ddd}</Data></Cell>");
            xml.Append("</Row>");
            foreach (var employee in records.GroupBy(x => x.EmployeeId).OrderBy(x => x.First().EmployeeName))
            {
                var first = employee.First(); var summary = summaries[employee.Key]; var byDate = employee.ToDictionary(x => x.Date);
                xml.Append($"<Row ss:Height=\"90\"><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.EmpId)}</Data></Cell><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.EmployeeName)}</Data></Cell><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.Department)}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Present}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Absent}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Late}</Data></Cell><Cell><Data ss:Type=\"String\">{(int)summary.Work.TotalHours:D2}:{summary.Work.Minutes:D2}</Data></Cell>");
                var overtime = TimeSpan.FromTicks(employee.Sum(item => item.Overtime.Ticks));
                xml.Append($"<Cell><Data ss:Type=\"String\">{X(AttendanceRules.FormatHours(overtime))}</Data></Cell>");
                foreach (var day in dates)
                {
                    if (!byDate.TryGetValue(day, out var item))
                    {
                        xml.Append("<Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">—</Data></Cell>");
                        continue;
                    }
                    var style = item.Status == "Present" ? "Present" : item.Status == "Absent" ? "Absent" : "Late";
                    var code = X(item.Status);
                    var timing = item.CheckIn.HasValue ? $"&#10;{item.CheckIn:hh:mm tt}-{(item.CheckOut.HasValue ? item.CheckOut.Value.ToString("hh:mm tt") : "—")}" : string.Empty;
                    xml.Append($"<Cell ss:StyleID=\"{style}\"><Data ss:Type=\"String\">{code}{timing}&#10;{X(item.TotalHoursDisplay)}&#10;OT: {X(item.OvertimeDisplay)}&#10;{X(item.Remark)}</Data></Cell>");
                }
                xml.Append("</Row>");
            }
            xml.Append("</Table><WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\"><FreezePanes/><FrozenNoSplit/><SplitHorizontal>2</SplitHorizontal><TopRowBottomPane>2</TopRowBottomPane><SplitVertical>3</SplitVertical><LeftColumnRightPane>3</LeftColumnRightPane><ProtectObjects>False</ProtectObjects><ProtectScenarios>False</ProtectScenarios></WorksheetOptions></Worksheet>");
            xml.Append("<Worksheet ss:Name=\"Daily Details\"><Table><Column ss:Width=\"75\"/><Column ss:Width=\"140\" ss:Span=\"1\"/><Column ss:Width=\"85\" ss:Span=\"5\"/><Column ss:Width=\"360\" ss:Span=\"1\"/><Row ss:StyleID=\"Header\">");
            foreach (var heading in new[] { "Emp ID", "Employee Name", "Department", "Date", "Check In", "Check Out", "Total Hours", "Overtime", "Status", "Site Check In - Time / Location / GPS", "Site Check Out - Time / Location / GPS" })
                xml.Append($"<Cell><Data ss:Type=\"String\">{X(heading)}</Data></Cell>");
            xml.Append("</Row>");
            foreach (var item in records.OrderBy(item => item.EmployeeName).ThenBy(item => item.EmployeeId).ThenBy(item => item.Date))
            {
                xml.Append("<Row>");
                foreach (var value in new[] { item.EmpId, item.EmployeeName, item.Department, item.Date.ToString("dd-MMM-yyyy"), item.CheckIn?.ToString("hh:mm tt") ?? "", item.CheckOut?.ToString("hh:mm tt") ?? "", item.TotalHoursDisplay, item.OvertimeDisplay, item.Status, item.SiteCheckIns, item.SiteCheckOuts })
                    xml.Append($"<Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(value)}</Data></Cell>");
                xml.Append("</Row>");
            }
            xml.Append("</Table></Worksheet></Workbook>");
            return File(new UTF8Encoding(true).GetBytes(xml.ToString()), "application/vnd.ms-excel", $"Attendance-Pivot-{month:yyyy-MM}.xls");
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> AddAttendance()
        {
            return View(await BuildManualAttendanceViewModelAsync(new ManualAttendanceViewModel()));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> AddAttendance(ManualAttendanceViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Remarks) || model.Remarks.Length > 300) ModelState.AddModelError(nameof(model.Remarks), "Enter a reason of up to 300 characters.");
            if (string.IsNullOrWhiteSpace(model.Department)) ModelState.AddModelError(nameof(model.Department), "Select a department.");
            if (!model.EmployeeId.HasValue) ModelState.AddModelError(nameof(model.EmployeeId), "Select an employee.");
            if (!model.CheckInTime.HasValue) ModelState.AddModelError(nameof(model.CheckInTime), "Enter check-in time.");
            if (model.CheckOutTime.HasValue && model.CheckInTime.HasValue && model.CheckOutTime <= model.CheckInTime)
                ModelState.AddModelError(nameof(model.CheckOutTime), "Check-out time must be after check-in time.");
            if (model.AttendanceDate > DateOnly.FromDateTime(DateTime.Today))
                ModelState.AddModelError(nameof(model.AttendanceDate), "Future attendance cannot be marked.");

            var employee = model.EmployeeId.HasValue
                ? await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == model.EmployeeId && item.IsActive)
                : null;
            if (employee == null || !string.Equals(employee.Department, model.Department, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(model.EmployeeId), "The selected employee does not belong to this department.");

            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                var managerDepartment = await _dbContext.Employees.AsNoTracking()
                    .Where(item => item.Id == managerId).Select(item => item.Department).FirstOrDefaultAsync();
                if (employee == null || string.IsNullOrWhiteSpace(managerDepartment)
                    || !string.Equals(employee.Department, managerDepartment, StringComparison.OrdinalIgnoreCase))
                    return Forbid();
            }

            if (employee != null && model.CheckInTime.HasValue)
            {
                var dayStart = model.AttendanceDate.ToDateTime(TimeOnly.MinValue);
                var dayEnd = dayStart.AddDays(1);
                if (await _dbContext.AttendanceLogs.AnyAsync(log => log.EmployeeId == employee.Id && log.PunchTime >= dayStart && log.PunchTime < dayEnd))
                    ModelState.AddModelError(string.Empty, "Attendance already exists for this employee on the selected date.");
            }

            if (!ModelState.IsValid) return View(await BuildManualAttendanceViewModelAsync(model));

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
            _dbContext.AttendanceRequests.Add(new AttendanceRequest
            {
                EmployeeId = employee!.Id, AttendanceDate = model.AttendanceDate,
                CheckInTime = model.CheckInTime!.Value, CheckOutTime = model.CheckOutTime,
                Reason = model.Remarks!.Trim(), RequestedByUserId = actorUserId
            });
            try { await _dbContext.SaveChangesAsync(); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                ModelState.AddModelError(string.Empty, "A pending request already exists for this employee and date.");
                return View(await BuildManualAttendanceViewModelAsync(model));
            }
            TempData["AttendanceRequestMessage"] = "Request sent to Admin. Attendance will be marked only after approval.";
            return RedirectToAction("Index", "AttendanceRequests");
        }
        private async Task<ManualAttendanceViewModel> BuildManualAttendanceViewModelAsync(ManualAttendanceViewModel model)
        {
            var employeeQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                var managerDepartment = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerId).Select(employee => employee.Department).FirstOrDefaultAsync();
                employeeQuery = employeeQuery.Where(employee => employee.Id != managerId && employee.Department == managerDepartment);
                model.IsManagerView = true;
            }
            model.Employees = await employeeQuery.OrderBy(employee => employee.FullName).ToListAsync();
            model.Departments = model.Employees.Select(employee => employee.Department).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            return model;
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Hrms()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var tomorrow = DateTime.Today.AddDays(1);
            var activeEmployeeIds = _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .Select(employee => employee.Id);

            var activeEmployeeIdList = await activeEmployeeIds.ToListAsync();
            var presentEmployeeIds = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue
                    && activeEmployeeIdList.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= DateTime.Today
                    && log.PunchTime < tomorrow)
                .Select(log => log.EmployeeId!.Value)
                .Distinct()
                .ToListAsync();
            var onLeaveEmployeeIds = await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved"
                    && activeEmployeeIdList.Contains(request.EmployeeId)
                    && request.FromDate <= today
                    && request.ToDate >= today)
                .Select(request => request.EmployeeId)
                .Distinct()
                .ToListAsync();

            var model = new HrmsDashboardViewModel
            {
                TotalEmployees = activeEmployeeIdList.Count,
                PresentToday = presentEmployeeIds.Count,
                OnLeaveToday = onLeaveEmployeeIds.Count,
                AbsentToday = AttendanceRules.IsWeeklyOff(today) ? 0 : activeEmployeeIdList.Except(presentEmployeeIds).Except(onLeaveEmployeeIds).Count()
            };

            return View(model);
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> Manager()
        {
            var managersQuery = _dbContext.Employees.AsNoTracking()
                .Include(employee => employee.DepartmentEntity)
                .Include(employee => employee.ReportingManager)
                .Where(employee => employee.IsActive && _dbContext.AppUsers
                    .Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"));
            if (User.IsInRole("Manager"))
            {
                var loggedInManagerId = await GetLoggedInEmployeeIdAsync();
                managersQuery = managersQuery.Where(employee => employee.Id == loggedInManagerId);
            }
            var managers = await managersQuery.OrderBy(employee => employee.FirstName).ThenBy(employee => employee.LastName).ToListAsync();

            var managerIds = managers.Select(manager => manager.Id).ToList();
            var managerLogin = User.IsInRole("Manager");
            var teamMembers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && employee.ReportingManagerId.HasValue
                    && employee.Id != employee.ReportingManagerId.Value
                    && managerIds.Contains(employee.ReportingManagerId.Value))
                .OrderBy(employee => employee.FullName)
                .ToListAsync();
            var projects = await _dbContext.Projects.AsNoTracking()
                .Where(project => project.ManagerId.HasValue && managerIds.Contains(project.ManagerId.Value))
                .OrderBy(project => project.ProjectName).ToListAsync();
            var projectIds = projects.Select(project => project.Id).ToList();
            var projectAssignments = await _dbContext.ProjectEmployees.AsNoTracking()
                .Include(assignment => assignment.Employee)
                .Where(assignment => projectIds.Contains(assignment.ProjectId) && assignment.Employee.IsActive)
                .OrderBy(assignment => assignment.Employee.FullName).ToListAsync();
            var teamMemberIds = teamMembers.Select(employee => employee.Id).ToList();
            var tasks = await _dbContext.WorkTasks.AsNoTracking()
                .Include(task => task.Manager).Include(task => task.Assignee)
                .Where(task => managerIds.Contains(task.ManagerId) || teamMemberIds.Contains(task.AssigneeId))
                .OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
            var leaveRequests = await _dbContext.LeaveRequests.AsNoTracking()
                .Include(request => request.Employee)
                .Where(request => teamMemberIds.Contains(request.EmployeeId))
                .OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            var queryTickets = await _dbContext.QueryTickets.AsNoTracking().Include(ticket => ticket.Employee)
                .Where(ticket => managerIds.Contains(ticket.ReportingManagerId ?? 0)).OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            var expenseClaims = await _dbContext.ExpenseClaims.AsNoTracking().Include(claim => claim.Employee)
                .Where(claim => claim.Status == "Pending" && (managerIds.Contains(claim.ReportingManagerId ?? 0) || (!managerLogin && claim.RequiresHrApproval)))
                .OrderByDescending(claim => claim.SubmittedAtUtc).ToListAsync();
            var notifications = new List<DashboardNotification>();
            notifications.AddRange(leaveRequests.Where(x => x.Status == "Pending").Select(x => new DashboardNotification(
                "Leave", $"Leave request from {x.Employee.FullName}", $"{x.LeaveType}: {x.FromDate:dd MMM} - {x.ToDate:dd MMM}", managerLogin ? "/Main/ManagerLeaves" : "/Main/WorkflowManagement", x.AppliedAtUtc)));
            notifications.AddRange(queryTickets.Where(x => x.Status != "Resolved" && x.Status != "Closed").Select(x => new DashboardNotification(
                "Query", $"Query from {x.Employee.FullName}", x.Subject, managerLogin ? "/Main/ManagerQueries" : "/Main/WorkflowManagement", x.CreatedAtUtc)));
            notifications.AddRange(expenseClaims.Select(x => new DashboardNotification(
                "Expense", $"Expense claim from {x.Employee.FullName}", $"{x.Title} · ₹{x.Amount:N2}", "/Expense/Index", x.SubmittedAtUtc)));
            notifications.AddRange(tasks.Where(x => !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && x.DueDate < DateOnly.FromDateTime(DateTime.Today)).Select(x => new DashboardNotification(
                "Task", $"Overdue task: {x.Title}", $"Assigned to {x.Assignee.FullName} · due {x.DueDate:dd MMM}", "/Main/TaskMgm", x.CreatedAtUtc)));

            return View(new ManagerDashboardViewModel
            {
                Managers = managers,
                Projects = projects,
                ProjectAssignments = projectAssignments,
                TeamMembers = teamMembers,
                Tasks = tasks,
                LeaveRequests = leaveRequests,
                QueryTickets = queryTickets,
                ExpenseClaims = expenseClaims,
                Notifications = notifications.OrderByDescending(x => x.CreatedAtUtc).ToList()
            });
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddEmpHrm()
        {
            return RedirectToAction("HrAddEmp", "Hr");
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public IActionResult TaskMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> ProjectMgm()
        {
            var projects = _dbContext.Projects.AsNoTracking();
            if (!User.IsInRole("Admin") && !User.IsInRole("HR"))
            {
                var employeeId = await GetLoggedInEmployeeIdAsync();
                projects = projects.Where(p => employeeId.HasValue && p.ManagerId == employeeId);
            }
            var ids = await projects.Select(p => p.Id).ToListAsync();
            ViewBag.TotalProjects = ids.Count;
            ViewBag.RunningProjects = await projects.CountAsync(p => p.Status == "Active");
            ViewBag.ActiveTasks = await _dbContext.WorkTasks.CountAsync(t => t.ProjectId.HasValue && ids.Contains(t.ProjectId.Value) && t.Status != "Completed");
            ViewBag.CompletionRate = (ids.Count == 0 ? 0 : await projects.CountAsync(p => p.Status == "Completed") * 100 / ids.Count) + "%";
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddProjectMgm()
        {
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "projects" });
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult DocumentMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddDocMgmSave()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> AdminPanel()
        {
            return View(await _dbContext.ModuleStates.AsNoTracking().ToListAsync());
        }

        [Authorize(Roles = "Admin")]
        public IActionResult AddAdminPanel()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Settings()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult DepartmentManagement()
        {
            return View();
        }

        [Authorize(Roles = "Employee,User,Admin,HR")]
        public IActionResult LeaveManagement()
        {
            if (User.IsInRole("HR") || User.IsInRole("Admin")) return RedirectToAction("Index", "AttendanceRequests");
            return View();
        }

        [HttpGet]
        [Authorize(Roles = "Admin,HR,Manager")]
        public IActionResult OrderTracking()
        {
            return View(new ShipmentTrackingPageViewModel { IsApiConfigured = _shipmentTrackingService.IsConfigured });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> OrderTracking(ShipmentTrackingPageViewModel model, CancellationToken cancellationToken)
        {
            model.IsApiConfigured = _shipmentTrackingService.IsConfigured;
            if (!ModelState.IsValid) return View(model);
            if (!model.IsApiConfigured)
            {
                // The page already shows the configuration notice. Avoid a second,
                // duplicate service error when credentials have not been installed.
                return View(model);
            }
            var result = await _shipmentTrackingService.TrackAsync(model.TrackingNumber, cancellationToken);
            model.Shipment = result.Shipment;
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerAttendance()
        {
            return View(await BuildManagerAttendanceAsync());
        }

        [HttpGet]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerAttendanceLive()
        {
            var model = await BuildManagerAttendanceAsync();
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var teamIds = model.TeamMembers.Select(employee => employee.Id).ToList();
            var lastPunches = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue && teamIds.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= today && log.PunchTime < tomorrow)
                .GroupBy(log => log.EmployeeId!.Value)
                .Select(group => new { EmployeeId = group.Key, LastPunch = group.Max(log => log.PunchTime) })
                .ToDictionaryAsync(item => item.EmployeeId, item => item.LastPunch);

            return Json(new
            {
                refreshedAt = DateTime.Now.ToString("hh:mm:ss tt"),
                teamMembers = model.TeamMembers.Select(employee => new
                {
                    employee.Id,
                    employee.FullName,
                    employee.Email,
                    employee.EmployeeCode,
                    employee.Department,
                    employee.Designation,
                    status = model.OnLeaveIds.Contains(employee.Id) ? "On Leave" : model.PresentIds.Contains(employee.Id) ? "Present" : model.IsWeeklyOff ? "Sunday Off" : "Absent",
                    lastPunch = lastPunches.TryGetValue(employee.Id, out var punch) ? punch.ToString("hh:mm tt") : null
                }),
                teamMembersCount = model.TeamMembers.Count,
                present = model.Present,
                absent = model.Absent,
                onLeave = model.OnLeave
            });
        }

        [HttpGet, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> ManagerNotificationCount(DateTime sinceUtc)
        {
            if (sinceUtc == default) sinceUtc = DateTime.UtcNow;
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                if (!managerId.HasValue) return Json(new { count = 0 });
                var newLeave = await _dbContext.LeaveRequests.AnyAsync(x => x.Status == "Pending" && x.AssignedApproverEmployeeId == managerId && x.AppliedAtUtc > sinceUtc);
                var newQuery = await _dbContext.QueryTickets.AnyAsync(x => x.ReportingManagerId == managerId && x.Status != "Resolved" && x.Status != "Closed" && x.CreatedAtUtc > sinceUtc);
                var newExpense = await _dbContext.ExpenseClaims.AnyAsync(x => !x.RequiresHrApproval && x.ReportingManagerId == managerId && x.Status == "Pending" && x.SubmittedAtUtc > sinceUtc);
                return Json(new { hasNew = newLeave || newQuery || newExpense });
            }
            var hrNewLeave = await _dbContext.LeaveRequests.AnyAsync(x => x.Status == "Pending" && x.AppliedAtUtc > sinceUtc);
            var hrNewQuery = await _dbContext.QueryTickets.AnyAsync(x => x.Status != "Resolved" && x.Status != "Closed" && x.CreatedAtUtc > sinceUtc);
            var hrNewExpense = await _dbContext.ExpenseClaims.AnyAsync(x => x.Status == "Pending" && x.SubmittedAtUtc > sinceUtc);
            return Json(new { hasNew = hrNewLeave || hrNewQuery || hrNewExpense });
        }

        [HttpGet, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> ManagerNotificationCount()
        {
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                if (!managerId.HasValue) return Json(new { count = 0 });
                var leaveCount = await _dbContext.LeaveRequests.CountAsync(x => x.Status == "Pending" && x.AssignedApproverEmployeeId == managerId);
                var queryCount = await _dbContext.QueryTickets.CountAsync(x => x.ReportingManagerId == managerId && x.Status != "Resolved" && x.Status != "Closed");
                var expenseCount = await _dbContext.ExpenseClaims.CountAsync(x => !x.RequiresHrApproval && x.ReportingManagerId == managerId && x.Status == "Pending");
                var overdueCount = await _dbContext.WorkTasks.CountAsync(x => x.ManagerId == managerId && x.Status != "Completed" && x.DueDate < DateOnly.FromDateTime(DateTime.Today));
                return Json(new { count = leaveCount + queryCount + expenseCount + overdueCount });
            }
            var hrLeaveCount = await _dbContext.LeaveRequests.CountAsync(x => x.Status == "Pending");
            var hrQueryCount = await _dbContext.QueryTickets.CountAsync(x => x.Status != "Resolved" && x.Status != "Closed");
            var hrExpenseCount = await _dbContext.ExpenseClaims.CountAsync(x => x.Status == "Pending");
            var hrOverdueCount = await _dbContext.WorkTasks.CountAsync(x => x.Status != "Completed" && x.DueDate < DateOnly.FromDateTime(DateTime.Today));
            return Json(new { count = hrLeaveCount + hrQueryCount + hrExpenseCount + hrOverdueCount });
        }

        private async Task<ManagerAttendanceViewModel> BuildManagerAttendanceAsync()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (!managerId.HasValue) return new ManagerAttendanceViewModel();
            var team = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && employee.ReportingManagerId == managerId.Value)
                .OrderBy(employee => employee.FullName).ToListAsync();
            var teamIds = team.Select(employee => employee.Id).ToList();
            var today = DateTime.Today;
            var presentIds = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId.HasValue && teamIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < today.AddDays(1)).Select(log => log.EmployeeId!.Value).Distinct().ToListAsync();
            var leaveIds = await _dbContext.LeaveRequests.AsNoTracking().Where(request => teamIds.Contains(request.EmployeeId) && request.Status == "Approved" && request.FromDate <= DateOnly.FromDateTime(today) && request.ToDate >= DateOnly.FromDateTime(today)).Select(request => request.EmployeeId).Distinct().ToListAsync();
            return new ManagerAttendanceViewModel { TeamMembers = team, PresentIds = presentIds.ToHashSet(), OnLeaveIds = leaveIds.ToHashSet(), IsWeeklyOff = AttendanceRules.IsWeeklyOff(DateOnly.FromDateTime(today)) };
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerLeaves()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (!managerId.HasValue) return RedirectToAction(nameof(AccessDenied));
            var requests = await _dbContext.LeaveRequests.AsNoTracking().Include(request => request.Employee)
                .Where(request => request.AssignedApproverEmployeeId == managerId && request.EmployeeId != managerId).OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            return View(new ManagerSectionViewModel { LeaveRequests = requests });
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerQueries()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Include(ticket => ticket.Employee)
                .Where(ticket => ticket.ReportingManagerId == managerId && ticket.Status != "Resolved" && ticket.Status != "Closed")
                .OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            return View(new ManagerSectionViewModel { QueryTickets = tickets });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager")]
        public async Task<IActionResult> DecideTeamLeave(int id, string? decision, string? note)
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (!managerId.HasValue || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
            var normalizedDecision = string.Equals(decision, "Approved", StringComparison.OrdinalIgnoreCase) ? "Approved"
                : string.Equals(decision, "Rejected", StringComparison.OrdinalIgnoreCase) ? "Rejected" : null;
            if (normalizedDecision == null || note?.Trim().Length > 500)
            {
                TempData["WorkflowMessage"] = "Select Approve or Reject. Decision notes must be 500 characters or fewer.";
                return RedirectToAction(nameof(ManagerLeaves));
            }
            var request = await _dbContext.LeaveRequests.Include(item => item.Employee).FirstOrDefaultAsync(item => item.Id == id && item.AssignedApproverEmployeeId == managerId && item.EmployeeId != managerId);
            if (request == null)
            {
                TempData["WorkflowMessage"] = "This leave request is no longer assigned to you. The list has been refreshed.";
                return RedirectToAction(nameof(ManagerLeaves));
            }
            if (request.Status != "Pending")
            {
                TempData["WorkflowMessage"] = $"This request has already been {request.Status.ToLowerInvariant()}.";
                return RedirectToAction(nameof(ManagerLeaves));
            }
            request.Status = normalizedDecision;
            request.DecidedByUserId = userId; request.DecidedAtUtc = DateTime.UtcNow; request.DecisionNote = CleanProfileValue(note);
            await _dbContext.SaveChangesAsync();
            TempData["WorkflowMessage"] = $"Leave request {request.Status.ToLowerInvariant()}. HR can now see the updated status.";
            return RedirectToAction(nameof(ManagerLeaves));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager,HR,Admin")]
        public async Task<IActionResult> UpdateQueryTicket(int id, string status, string? resolution)
        {
            var ticket = await _dbContext.QueryTickets.FirstOrDefaultAsync(item => item.Id == id);
            if (ticket == null) return NotFound();
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (User.IsInRole("Manager") && ticket.ReportingManagerId != managerId) return Forbid();
            ticket.Status = status is "Resolved" or "Closed" ? status : "In Progress";
            ticket.Resolution = CleanProfileValue(resolution);
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            ticket.ResolvedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(User.IsInRole("Manager") ? nameof(ManagerQueries) : nameof(WorkflowManagement));
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> SalarySlips()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var months = await _dbContext.GeneratedSalarySlips.AsNoTracking()
                .Where(x => x.EmployeeId == employee.Id)
                .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                .Select(x => new SalarySlipMonth(x.Year, x.Month, new DateTime(x.Year, x.Month, 1).ToString("MMMM yyyy"), true)).ToListAsync();
            return View(new SalarySlipPageViewModel { Employee = employee, Months = months });
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> DownloadSalarySlip(int year, int month)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (month is < 1 or > 12) return BadRequest("Invalid salary slip month.");
            var slip = await _dbContext.GeneratedSalarySlips.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employee.Id && x.Year == year && x.Month == month);
            if (slip == null) return NotFound("This salary slip has not been generated by HR.");
            var bank = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id);
            var pdf = SalarySlipPdfService.Create(employee, slip, bank);
            return File(pdf, "application/pdf", $"Salary-Slip-{employee.EmployeeCode}-{year}-{month:00}.pdf");
        }

        [Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> WorkflowManagement()
        {
            var leaves = await _dbContext.LeaveRequests.AsNoTracking().Include(item => item.Employee).Include(item => item.AssignedApproverEmployee)
                .Where(item => item.Status == "Pending").OrderByDescending(item => item.AppliedAtUtc).ToListAsync();
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Include(item => item.Employee).Include(item => item.ReportingManager).Include(item => item.ResolvedByUser)
                .Where(item => item.Status != "Resolved" && item.Status != "Closed")
                .OrderByDescending(item => item.CreatedAtUtc).ToListAsync();
            return View(new WorkflowManagementViewModel { LeaveRequests = leaves, QueryTickets = tickets });
        }

        [Authorize(Roles = "Manager,HR,Admin")]
        public async Task<IActionResult> ClosedIssues(string? search, string? department, string? status)
        {
            var isManager = User.IsInRole("Manager");
            var managerId = isManager ? await GetLoggedInEmployeeIdAsync() : null;
            var baseQuery = _dbContext.QueryTickets.AsNoTracking()
                .Where(ticket => (ticket.Status == "Resolved" || ticket.Status == "Closed")
                    && (!isManager || ticket.ReportingManagerId == managerId));
            var departments = await baseQuery.Select(ticket => ticket.Employee.Department)
                .Distinct().OrderBy(name => name).ToListAsync();
            IQueryable<QueryTicket> query = baseQuery.Include(ticket => ticket.Employee)
                .Include(ticket => ticket.ReportingManager)
                .Include(ticket => ticket.ResolvedByUser);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(ticket => ticket.Employee.FullName.Contains(term)
                    || ticket.Employee.EmployeeCode.Contains(term)
                    || ticket.Subject.Contains(term)
                    || ticket.Description.Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(ticket => ticket.Employee.Department == department);
            if (status is "Resolved" or "Closed")
                query = query.Where(ticket => ticket.Status == status);

            var tickets = await query.OrderByDescending(ticket => ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc).ToListAsync();
            return View(new ClosedIssuesViewModel
            {
                Tickets = tickets, Departments = departments, Search = search,
                Department = department, Status = status, IsManagerView = isManager
            });
        }

        [Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> LeaveStatus(string? search, string? department, string? status)
        {
            var baseQuery = _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved" || request.Status == "Rejected");
            var departments = await baseQuery.Select(request => request.Employee.Department)
                .Distinct().OrderBy(name => name).ToListAsync();
            IQueryable<LeaveRequest> query = baseQuery
                .Include(request => request.Employee)
                .Include(request => request.AssignedApproverEmployee)
                .Include(request => request.DecidedByUser);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(request => request.Employee.FullName.Contains(term)
                    || request.Employee.EmployeeCode.Contains(term)
                    || request.LeaveType.Contains(term)
                    || request.Reason.Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(request => request.Employee.Department == department);
            if (status is "Approved" or "Rejected")
                query = query.Where(request => request.Status == status);

            var requests = await query.OrderByDescending(request => request.DecidedAtUtc ?? request.AppliedAtUtc).ToListAsync();
            return View(new LeaveStatusViewModel
            {
                Requests = requests, Departments = departments, Search = search,
                Department = department, Status = status
            });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> DecideManagerLeave(int id, string decision, string? note)
        {
            var request = await _dbContext.LeaveRequests.Include(item => item.Employee).FirstOrDefaultAsync(item => item.Id == id && item.ApprovalLevel == "HR/Admin" && item.Status == "Pending");
            if (request == null) return NotFound();
            request.Status = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Rejected";
            request.DecidedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
            request.DecidedAtUtc = DateTime.UtcNow; request.DecisionNote = CleanProfileValue(note);
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(WorkflowManagement));
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerProjects()
        {
            await Task.CompletedTask;
            return RedirectToAction("Index", "ProjectWorkspace", new { section = "projects" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> ApplyLeave(string leaveType, DateOnly fromDate, DateOnly toDate, string reason)
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var employeeId = int.TryParse(userIdText, out var userId)
                ? await _dbContext.AppUsers.Where(user => user.Id == userId).Select(user => user.EmployeeId).FirstOrDefaultAsync()
                : null;
            if (!employeeId.HasValue)
            {
                TempData["LeaveError"] = "Your login is not linked to an employee profile.";
                return RedirectToAction(nameof(EmployeeLeaves));
            }
            if (string.IsNullOrWhiteSpace(leaveType) || string.IsNullOrWhiteSpace(reason) || fromDate < DateOnly.FromDateTime(DateTime.Today) || toDate < fromDate)
            {
                TempData["LeaveError"] = "Please enter a valid leave type, date range and reason.";
                return RedirectToAction(nameof(EmployeeLeaves));
            }
            var employee = await _dbContext.Employees.AsNoTracking().FirstAsync(item => item.Id == employeeId.Value);
            var seniorApplicant = User.IsInRole("Manager") || User.IsInRole("HR");
            _dbContext.LeaveRequests.Add(new LeaveRequest { EmployeeId = employee.Id, LeaveType = leaveType.Trim(), FromDate = fromDate, ToDate = toDate, Reason = reason.Trim(), ApprovalLevel = seniorApplicant ? "HR/Admin" : "Manager", AssignedApproverEmployeeId = seniorApplicant ? null : employee.ReportingManagerId });
            await _dbContext.SaveChangesAsync();
            TempData["LeaveMessage"] = "Leave request submitted to your manager.";
            return RedirectToAction(nameof(EmployeeLeaves));
        }

        public IActionResult AccessDenied()
        {
            var role = AccountRoleService.Normalize(User.FindFirstValue(ClaimTypes.Role));
            return role == null ? Forbid() : RedirectToRoleHome(role);
        }

        private IActionResult RedirectToRoleHome(string? role = null)
        {
            return AccountRoleService.Normalize(role ?? User.FindFirstValue(ClaimTypes.Role)) switch
            {
                AccountRoleService.Manager => RedirectToAction("Manager", "Main"),
                AccountRoleService.Employee => RedirectToAction("EmployeeHome", "Main"),
                AccountRoleService.HR => RedirectToAction("Dashboard", "Main"),
                AccountRoleService.Admin => RedirectToAction("Dashboard", "Main"),
                _ => RedirectToAction("AccessDenied", "Main")
            };
        }

        private async Task<int?> GetLoggedInEmployeeIdAsync()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdText, out var userId)
                ? await _dbContext.AppUsers.AsNoTracking().Where(user => user.Id == userId && user.IsActive).Select(user => user.EmployeeId).FirstOrDefaultAsync()
                : null;
        }

        private async Task<string?> SaveFieldAttendanceSelfieAsync(string? dataUrl, int employeeId, string action)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return null;
            const string prefix = "data:image/";
            if (!dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !dataUrl.Contains(";base64,"))
                throw new InvalidOperationException("Invalid selfie image.");
            var encoded = dataUrl[(dataUrl.IndexOf(";base64,", StringComparison.Ordinal) + 8)..];
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length is 0 or > 5 * 1024 * 1024)
                throw new InvalidOperationException("Selfie must be smaller than 5 MB.");

            var folder = Path.Combine(_environment.WebRootPath, "uploads", "field-attendance");
            Directory.CreateDirectory(folder);
            var fileName = $"{employeeId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{action.Replace(" ", "").ToLowerInvariant()}-{Guid.NewGuid():N}.jpg";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
            return $"/uploads/field-attendance/{fileName}";
        }

        private async Task<Employee?> LoadLoggedInEmployeeAsync()
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue && AccountRoleService.Normalize(User.FindFirstValue(ClaimTypes.Role)) == AccountRoleService.HR)
            {
                employeeId = await EnsureHrSelfServiceProfileAsync();
            }
            return employeeId.HasValue
                ? await _dbContext.Employees.AsNoTracking().Include(employee => employee.ReportingManager).Include(employee => employee.DepartmentEntity).FirstOrDefaultAsync(employee => employee.Id == employeeId.Value && employee.IsActive)
                : null;
        }

        private async Task<int?> EnsureHrSelfServiceProfileAsync()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdText, out var userId)) return null;

            var user = await _dbContext.AppUsers.FirstOrDefaultAsync(item => item.Id == userId && item.IsActive);
            if (user == null || AccountRoleService.Normalize(user.Role) != AccountRoleService.HR) return null;
            if (user.EmployeeId.HasValue) return user.EmployeeId;

            var normalizedName = user.FullName.Trim().ToLower();
            var normalizedUsername = user.Username.Trim().ToLower();
            var existingEmployee = await _dbContext.Employees.FirstOrDefaultAsync(employee =>
                employee.IsActive &&
                (employee.FullName.ToLower() == normalizedName || employee.Email.ToLower() == normalizedUsername));

            if (existingEmployee == null)
            {
                var baseCode = $"HR-{user.Id:0000}";
                var employeeCode = baseCode;
                var suffix = 1;
                while (await _dbContext.Employees.AnyAsync(employee => employee.EmployeeCode == employeeCode))
                {
                    employeeCode = $"{baseCode}-{suffix++}";
                }

                var email = user.Username.Contains('@') ? user.Username.Trim() : $"{user.Username.Trim()}@vertex.local";
                existingEmployee = new Employee
                {
                    EmployeeCode = employeeCode,
                    FirstName = string.IsNullOrWhiteSpace(user.FullName) ? "HR" : user.FullName.Trim(),
                    FullName = string.IsNullOrWhiteSpace(user.FullName) ? "HR" : user.FullName.Trim(),
                    Email = email,
                    PhoneNumber = "Not provided",
                    EmergencyContact = "Not provided",
                    JoiningDate = DateOnly.FromDateTime(DateTime.Today),
                    Department = "Human Resources",
                    Designation = "HR Executive",
                    EmploymentType = "Full Time",
                    EmployeeStatus = "Active",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };
                _dbContext.Employees.Add(existingEmployee);
                await _dbContext.SaveChangesAsync();
            }

            user.EmployeeId = existingEmployee.Id;
            await _dbContext.SaveChangesAsync();
            return existingEmployee.Id;
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> MyBankDetails()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var detail = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employee.Id);
            var requests = await _dbContext.BankDetailUpdateRequests.AsNoTracking().Where(x => x.EmployeeId == employee.Id).OrderByDescending(x => x.RequestedAtUtc).ToListAsync();
            return View(new MyBankDetailsViewModel { Employee = employee, BankDetail = detail, Requests = requests, UpdateRequest = new BankDetailRequestViewModel { AccountHolderName = detail?.AccountHolderName ?? employee.FullName, BankName = detail?.BankName ?? string.Empty, IfscCode = detail?.IfscCode ?? string.Empty, BranchName = detail?.BranchName, AccountType = detail?.AccountType ?? "Savings", PanNumber = detail?.PanNumber, UanNumber = detail?.UanNumber, EsicNumber = detail?.EsicNumber, UpiId = detail?.UpiId } });
        }

        [HttpGet, Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> RequestBankUpdate()
        {
            return await LoadLoggedInEmployeeAsync() == null ? RedirectToAction(nameof(AccessDenied)) : View(new BankDetailRequestViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> RequestBankUpdate(BankDetailRequestViewModel model)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (!ModelState.IsValid) return View(model);
            if (await _dbContext.BankDetailUpdateRequests.AnyAsync(x => x.EmployeeId == employee.Id && x.Status == "Pending"))
            { ModelState.AddModelError(string.Empty, "A bank update request is already pending with HR."); return View(model); }
            var account = model.AccountNumber.Trim();
            _dbContext.BankDetailUpdateRequests.Add(new BankDetailUpdateRequest { EmployeeId = employee.Id, AccountHolderName = model.AccountHolderName.Trim(), BankName = model.BankName.Trim(), ProtectedAccountNumber = _bankProtection.Protect(account), AccountLastFour = account[^4..], IfscCode = model.IfscCode.Trim().ToUpperInvariant(), BranchName = CleanProfileValue(model.BranchName), AccountType = model.AccountType, PanNumber = CleanProfileValue(model.PanNumber)?.ToUpperInvariant(), UanNumber = CleanProfileValue(model.UanNumber), EsicNumber = CleanProfileValue(model.EsicNumber), UpiId = CleanProfileValue(model.UpiId) });
            await _dbContext.SaveChangesAsync(); TempData["BankMessage"] = "Bank detail request submitted to HR for verification."; return RedirectToAction(nameof(MyBankDetails));
        }

        [Authorize(Roles = "HR")]
        public async Task<IActionResult> BankUpdateRequests()
        {
            var requests = await _dbContext.BankDetailUpdateRequests.AsNoTracking().Include(x => x.Employee).OrderByDescending(x => x.RequestedAtUtc).ToListAsync();
            return View(new BankApprovalViewModel { Requests = requests });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR")]
        public async Task<IActionResult> ReviewBankUpdate(int id, string decision, string? note)
        {
            var request = await _dbContext.BankDetailUpdateRequests.FirstOrDefaultAsync(x => x.Id == id && x.Status == "Pending");
            if (request == null) return NotFound();
            request.Status = decision == "Approved" ? "Approved" : "Rejected"; request.HrNote = CleanProfileValue(note); request.ReviewedAtUtc = DateTime.UtcNow; request.ReviewedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
            if (request.Status == "Approved")
            {
                var detail = await _dbContext.EmployeeBankDetails.FirstOrDefaultAsync(x => x.EmployeeId == request.EmployeeId) ?? new EmployeeBankDetail { EmployeeId = request.EmployeeId };
                detail.AccountHolderName = request.AccountHolderName; detail.BankName = request.BankName; detail.ProtectedAccountNumber = request.ProtectedAccountNumber; detail.AccountLastFour = request.AccountLastFour; detail.IfscCode = request.IfscCode; detail.BranchName = request.BranchName; detail.AccountType = request.AccountType; detail.PanNumber = request.PanNumber; detail.UanNumber = request.UanNumber; detail.EsicNumber = request.EsicNumber; detail.UpiId = request.UpiId; detail.IsVerified = true; detail.VerifiedByUserId = request.ReviewedByUserId; detail.VerifiedAtUtc = DateTime.UtcNow; detail.UpdatedAtUtc = DateTime.UtcNow;
                if (detail.Id == 0) _dbContext.EmployeeBankDetails.Add(detail);
            }
            await _dbContext.SaveChangesAsync(); return RedirectToAction(nameof(BankUpdateRequests));
        }

        private async Task<EmployeeProfileEditViewModel> PopulateEmployeeProfileOptionsAsync(EmployeeProfileEditViewModel model, int employeeId)
        {
            model.Departments = await _dbContext.Departments.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.DepartmentName).ToListAsync();
            model.Managers = await _dbContext.Employees.AsNoTracking().Where(item => item.IsActive && item.Id != employeeId && _dbContext.AppUsers.Any(user => user.EmployeeId == item.Id && user.IsActive && user.Role == "Manager")).OrderBy(item => item.FullName).ToListAsync();
            return model;
        }

        private static string? CleanProfileValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
