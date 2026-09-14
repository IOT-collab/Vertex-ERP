using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VertexERP.Controllers;
using VertexERP.Models;
using VertexERP.Services;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
bool Valid(object model) => Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);
Check(Valid(new ForgotPasswordViewModel { PhoneNumber = "7042480745" }), "10-digit mobile accepted");
Check(!Valid(new ForgotPasswordViewModel { PhoneNumber = "person@example.com" }), "Email rejected");
Check(!Valid(new VerifyResetOtpViewModel { PhoneNumber = "7042480745", Otp = "12345" }), "Short OTP rejected");
Check(!Valid(new ResetPasswordViewModel { PhoneNumber = "7042480745", NewPassword = "long-password", ConfirmPassword = "different" }), "Password mismatch rejected");
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Sms:Infobip:ApiKey"] = "test-key" }).Build();
var handler = new StubHandler();
var sms = new PasswordResetSmsService(new HttpClient(handler), configuration);
await sms.SendOtpAsync("7042480745", "123456");
Check(handler.ValidRequest, "Infobip authorization, destination, endpoint and five-minute SMS text");
handler.GroupId = 5;
try { await sms.SendOtpAsync("7042480745", "123456"); throw new Exception("Rejection accepted"); } catch (InvalidOperationException) { Check(true, "Provider rejection handled even on HTTP 200"); }
handler.StatusCode = HttpStatusCode.Unauthorized;
try { await sms.SendOtpAsync("7042480745", "123456"); throw new Exception("Unauthorized accepted"); } catch (HttpRequestException) { Check(true, "Invalid API key response handled"); }
Check(!new PasswordResetSmsService(new HttpClient(handler), new ConfigurationBuilder().Build()).IsConfigured, "Missing credentials fail closed");
var context = new DefaultHttpContext { Session = new TestSession() };
var controller = new MainController(null!, null!, null!, null!, null!, sms) { ControllerContext = new ControllerContext { HttpContext = context } };
Check(controller.ResetPassword() is RedirectToActionResult, "Reset page requires verification");
context.Session.SetString("PasswordResetPhoneNumber", "7042480745");
context.Session.SetInt32("PasswordResetUserId", 1);
context.Session.SetString("PasswordResetHash", "test-hash");
context.Session.SetString("PasswordResetDeadline", DateTime.UtcNow.AddMinutes(-1).ToString("O"));
Check(controller.ResetPassword() is RedirectToActionResult, "Expired verification cannot open reset page");
context.Session.SetString("PasswordResetDeadline", DateTime.UtcNow.AddMinutes(5).ToString("O"));
Check(controller.ResetPassword() is ViewResult, "Unexpired verification opens reset page");
if (args.Length == 1) await DatabaseChecks.Run(args[0], Check);
Console.WriteLine($"{passed} checks passed. No SMS sent by this test suite.");

sealed class StubHandler : HttpMessageHandler
{
    public int GroupId = 1;
    public HttpStatusCode StatusCode = HttpStatusCode.OK;
    public bool ValidRequest;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var message = body.RootElement.GetProperty("messages")[0];
        ValidRequest = request.RequestUri!.AbsoluteUri == "https://api.infobip.com/sms/3/messages" &&
            request.Headers.Authorization?.ToString() == "App test-key" &&
            message.GetProperty("destinations")[0].GetProperty("to").GetString() == "917042480745" &&
            message.GetProperty("content").GetProperty("text").GetString()!.Contains("123456. Valid for 5 minutes.");
        return new HttpResponseMessage(StatusCode) { Content = new StringContent($"{{\"messages\":[{{\"status\":{{\"groupId\":{GroupId}}}}}]}}") };
    }
}
sealed class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> values = new();
    public bool IsAvailable => true;
    public string Id => "test";
    public IEnumerable<string> Keys => values.Keys;
    public void Clear() => values.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => values.Remove(key);
    public void Set(string key, byte[] value) => values[key] = value;
    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
}
