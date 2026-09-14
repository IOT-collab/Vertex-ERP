using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

await Check("disabled configuration", new() { Enabled = false }, null, "Disabled", "disabled");
await Check("missing credentials", new() { Enabled = true }, null, "Configuration error", "credentials");
await Check("invalid source", new() { Enabled = true, BaseUrl = "invalid" }, null, "Configuration error", "address");
await Check("source unreachable", Valid(), new HttpRequestException("private network detail"), "Failed", "Cannot reach");
await Check("request timeout", Valid(), new TaskCanceledException("private timeout detail"), "Failed", "timed out");
await Check("source rejects access", Valid(), new HttpRequestException("private login detail", null, HttpStatusCode.Forbidden), "Failed", "403");
await Check("login rejected", Valid(), null, "Failed", "Connecting");
Console.WriteLine("PASS: 7 attendance configuration and connection failure checks. No production data accessed.");

static RemoteAttendanceOptions Valid() => new() { Enabled = true, BaseUrl = "https://attendance.invalid/", Username = "test", Password = "secret-not-for-status" };

static async Task Check(string name, RemoteAttendanceOptions options, Exception? error, string state, string message)
{
    using var provider = new ServiceCollection().BuildServiceProvider();
    using var client = new HttpClient(new StubHandler(error));
    using var service = new RemoteAttendanceImportService(provider.GetRequiredService<IServiceScopeFactory>(),
        new ClientFactory(client), Options.Create(options), NullLogger<RemoteAttendanceImportService>.Instance);
    await service.StartAsync(CancellationToken.None);
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while ((service.Status.State == "Starting" || service.Status.State == "Syncing") && DateTime.UtcNow < deadline)
        await Task.Delay(10);
    var status = service.Status;
    await service.StopAsync(CancellationToken.None);
    if (status.State != state || !status.Message.Contains(message, StringComparison.OrdinalIgnoreCase)
        || status.LastSuccessUtc != null || status.Message.Contains("secret-not-for-status") || status.Message.Contains("private"))
        throw new Exception($"FAILED {name}: {status}");
    Console.WriteLine($"PASS: {name}");
}

sealed class ClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
sealed class StubHandler(Exception? error) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (error != null) return Task.FromException<HttpResponseMessage>(error);
        // A login page without a CSRF token must fail before any database work.
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Login unavailable"), RequestMessage = request });
    }
}
