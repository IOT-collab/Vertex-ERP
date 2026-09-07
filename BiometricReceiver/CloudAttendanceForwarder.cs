using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class CloudAttendanceForwardingOptions
{
    public const string SectionName = "CloudAttendanceForwarding";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int RetryIntervalSeconds { get; set; } = 30;
}

// Durable local outbox: an Azure outage cannot make the biometric machine lose punches.
public sealed class CloudAttendanceForwarder : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CloudAttendanceForwardingOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudAttendanceForwarder> _logger;
    private readonly string _outboxPath;

    public CloudAttendanceForwarder(IOptions<CloudAttendanceForwardingOptions> options, IHttpClientFactory httpClientFactory,
        IHostEnvironment environment, ILogger<CloudAttendanceForwarder> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _outboxPath = Path.Combine(environment.ContentRootPath, "App_Data", "CloudAttendanceOutbox");
        Directory.CreateDirectory(_outboxPath);
    }

    public async Task EnqueueAsync(string serialNumber, string payload, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        var item = new CloudAttendanceOutboxItem(serialNumber, payload, DateTime.UtcNow);
        var file = Path.Combine(_outboxPath, $"{item.CreatedAtUtc:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.json");
        var temporaryFile = file + ".tmp";
        await File.WriteAllTextAsync(temporaryFile, JsonSerializer.Serialize(item, JsonOptions), cancellationToken);
        File.Move(temporaryFile, file);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ForwardPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Cloud attendance outbox processing failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _options.RetryIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ForwardPendingAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey)) return;
        foreach (var file in Directory.EnumerateFiles(_outboxPath, "*.json").OrderBy(path => path))
        {
            var item = JsonSerializer.Deserialize<CloudAttendanceOutboxItem>(await File.ReadAllTextAsync(file, cancellationToken), JsonOptions);
            if (item is null) { File.Delete(file); continue; }
            var uri = new UriBuilder(endpoint) { Query = $"SN={Uri.EscapeDataString(item.SerialNumber)}" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(item.Payload, Encoding.UTF8, "text/plain")
            };
            request.Headers.Add("X-Biometric-Gateway-Key", _options.ApiKey);
            using var response = await _httpClientFactory.CreateClient("CloudAttendanceForwarding").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cloud attendance forwarding returned {StatusCode}; retaining the outbox item for retry.", response.StatusCode);
                break;
            }
            File.Delete(file);
        }
    }

    private sealed record CloudAttendanceOutboxItem(string SerialNumber, string Payload, DateTime CreatedAtUtc);
}
