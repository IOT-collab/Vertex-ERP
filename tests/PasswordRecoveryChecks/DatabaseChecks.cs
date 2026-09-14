using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class DatabaseChecks
{
    public static async Task Run(string settingsPath, Action<bool,string> check)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "otp_check_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString, o => o.EnableRetryOnFailure()).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            var employee = new Employee { EmployeeCode = "OTPTEST", FirstName = "Test", FullName = "Test", Email = "otp@example.invalid", PhoneNumber = "7000000001", JoiningDate = new DateOnly(2026,1,1), Department = "Test", Designation = "Test" };
            db.AppUsers.Add(new AppUser { Username = "otp-test", NormalizedUsername = "OTP-TEST", FullName = "Test", Role = "Employee", Employee = employee, PasswordHash = PasswordHashService.HashPassword("old-password") });
            await db.SaveChangesAsync();
            var session = new TestSession();
            var sms = new CapturingSms();
            MainController Controller()
            {
                var http = new DefaultHttpContext { Session = session };
                return new MainController(db, null!, null!, null!, null!, sms) { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new MemoryTempData()) };
            }
            var result = await Controller().ForgotPassword(new() { PhoneNumber = employee.PhoneNumber });
            check(result is RedirectToActionResult { ActionName: "VerifyResetOtp" } && sms.Count == 1, "Database retry enabled: send OTP succeeds once");
            var token = await db.PasswordResetTokens.AsNoTracking().SingleAsync();
            check(token.ExpiresAtUtc - token.CreatedAtUtc == TimeSpan.FromMinutes(5), "Database OTP lifetime exactly five minutes");
            check(await Controller().ForgotPassword(new() { PhoneNumber = employee.PhoneNumber }) is ViewResult && sms.Count == 1, "Resend cooldown prevents second SMS");
            session.SetInt32("ResetOtpTokenId", token.Id);
            check(await Controller().VerifyResetOtp(new VerifyResetOtpViewModel() { PhoneNumber = employee.PhoneNumber, Otp = "not-valid" }) is ViewResult, "Incorrect OTP rejected with retries enabled");
            check(await Controller().VerifyResetOtp(new VerifyResetOtpViewModel() { PhoneNumber = employee.PhoneNumber, Otp = sms.Otp }) is RedirectToActionResult { ActionName: "ResetPassword" }, "Correct OTP verified with retries enabled");
            check(await Controller().ResetPassword(new() { PhoneNumber = employee.PhoneNumber, NewPassword = "replacement-password", ConfirmPassword = "replacement-password" }) is RedirectToActionResult { ActionName: "Login" }, "Password reset transaction succeeds with retries enabled");
            db.ChangeTracker.Clear();
            check(PasswordHashService.VerifyPassword("replacement-password", (await db.AppUsers.SingleAsync()).PasswordHash), "New password persisted");
            session.SetInt32("ResetOtpTokenId", token.Id);
            check(await Controller().VerifyResetOtp(new VerifyResetOtpViewModel() { PhoneNumber = employee.PhoneNumber, Otp = sms.Otp }) is ViewResult, "Consumed OTP cannot be reused");
        }
        finally
        {
            // Only this run's randomly named test schema is removed.
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }
    sealed class CapturingSms : IPasswordResetSmsService
    {
        public bool IsConfigured => true;
        public int Count; public string Otp = "";
        public Task SendOtpAsync(string phone, string otp, CancellationToken cancellationToken = default) { Count++; Otp = otp; return Task.CompletedTask; }
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}

