using System.Globalization;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VertexERP.Data;
using VertexERP.Models;

public sealed class RemoteAttendanceOptions
{
    public const string SectionName = "RemoteAttendance";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://122.176.49.74:8082/";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
    public int PageSize { get; set; } = 1000;
}

public sealed class RemoteAttendanceImportService : BackgroundService
{
    public const string HttpClientName = "RemoteAttendance";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RemoteAttendanceOptions _options;
    private readonly ILogger<RemoteAttendanceImportService> _logger;
    private readonly string _checkpointPath = Path.Combine(AppContext.BaseDirectory, "remote-attendance-checkpoint.json");

    public RemoteAttendanceImportService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<RemoteAttendanceOptions> options,
        ILogger<RemoteAttendanceImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogError("Remote attendance is enabled but its credentials are missing.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ImportAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Remote attendance import failed; it will be retried."); }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, _options.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        var baseUri = new Uri(_options.BaseUrl, UriKind.Absolute);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        await LoginAsync(client, baseUri, cancellationToken);

        var terminals = await GetTerminalsAsync(client, baseUri, cancellationToken);
        await EnsureDevicesAsync(terminals, cancellationToken);

        var checkpoint = await LoadCheckpointAsync(cancellationToken);
        var transactionUri = new Uri(baseUri, "iclock/api/transactions/");
        var query = $"page_size={Math.Clamp(_options.PageSize, 1, 1000)}&ordering=punch_time%2Cid";
        if (checkpoint is not null)
            query += $"&start_time={Uri.EscapeDataString(checkpoint.PunchTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}";
        var next = new UriBuilder(transactionUri) { Query = query }.Uri;
        var imported = 0;
        var maxCheckpoint = checkpoint;

        while (next is not null)
        {
            using var response = await client.GetAsync(next, cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<ApiPage<RemoteTransaction>>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Remote transaction API returned an empty response.");
            imported += await ImportPageAsync(page.Data, cancellationToken);

            foreach (var item in page.Data)
            {
                if (!TryParseDate(item.PunchTime, out var time)) continue;
                if (maxCheckpoint is null || time > maxCheckpoint.PunchTime || (time == maxCheckpoint.PunchTime && item.Id > maxCheckpoint.TransactionId))
                    maxCheckpoint = new ImportCheckpoint(time, item.Id);
            }

            next = ValidateNextUri(baseUri, page.Next);
        }

        if (maxCheckpoint is not null) await SaveCheckpointAsync(maxCheckpoint, cancellationToken);
        _logger.LogInformation("Remote attendance sync completed: {Imported} new punches imported.", imported);
    }

    private async Task LoginAsync(HttpClient client, Uri baseUri, CancellationToken cancellationToken)
    {
        var loginUri = new Uri(baseUri, "api-auth/login/?next=/iclock/api/");
        using var loginPage = await client.GetAsync(loginUri, cancellationToken);
        loginPage.EnsureSuccessStatusCode();
        var html = await loginPage.Content.ReadAsStringAsync(cancellationToken);
        var match = Regex.Match(html, "name=[\\\"']csrfmiddlewaretoken[\\\"'][^>]*value=[\\\"'](?<token>[^\\\"']+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(html, "value=[\\\"'](?<token>[^\\\"']+)[\\\"'][^>]*name=[\\\"']csrfmiddlewaretoken[\\\"']", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Remote attendance login page did not provide a CSRF token.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _options.Username,
            ["password"] = _options.Password,
            ["csrfmiddlewaretoken"] = WebUtility.HtmlDecode(match.Groups["token"].Value),
            ["next"] = "/iclock/api/"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, loginUri) { Content = content };
        request.Headers.Referrer = loginUri;
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsolutePath.Contains("/api-auth/login/", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Remote attendance login was rejected.");
    }

    private static async Task<List<RemoteTerminal>> GetTerminalsAsync(HttpClient client, Uri baseUri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(baseUri, "iclock/api/terminals/?page_size=100"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ApiPage<RemoteTerminal>>(JsonOptions, cancellationToken);
        return page?.Data ?? [];
    }

    private async Task EnsureDevicesAsync(IEnumerable<RemoteTerminal> terminals, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var serials = terminals.Select(item => item.SerialNumber.Trim().ToUpperInvariant()).Where(value => value.Length > 0).Distinct().ToList();
        var existing = await db.BiometricDevices.Where(item => serials.Contains(item.SerialNumber)).ToDictionaryAsync(item => item.SerialNumber, StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var terminal in terminals)
        {
            var serial = terminal.SerialNumber.Trim().ToUpperInvariant();
            if (serial.Length == 0) continue;
            if (!existing.TryGetValue(serial, out var device))
            {
                device = new BiometricDevice
                {
                    SerialNumber = serial,
                    Name = Limit(terminal.Alias, 100) ?? serial,
                    Model = Limit(terminal.TerminalName, 80) ?? "Remote biometric terminal",
                    BranchCode = Limit(terminal.AreaName ?? terminal.Area?.Name, 100),
                    ServerAddress = new Uri(_options.BaseUrl).Host,
                    ServerPort = new Uri(_options.BaseUrl).Port,
                    CommunicationMode = "REMOTE_API",
                    FirmwareVersion = Limit(terminal.FirmwareVersion, 50),
                    Notes = "Automatically registered from the remote attendance API."
                };
                db.BiometricDevices.Add(device);
                existing[serial] = device;
            }
            device.IsActive = true;
            device.LastSeenUtc = DateTime.UtcNow;
            device.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ImportPageAsync(IReadOnlyList<RemoteTransaction> transactions, CancellationToken cancellationToken)
    {
        var valid = transactions.Select(item => (Item: item, Parsed: TryParseDate(item.PunchTime, out var value) ? value : (DateTime?)null))
            .Where(pair => pair.Parsed.HasValue && !string.IsNullOrWhiteSpace(pair.Item.EmployeeCode) && !string.IsNullOrWhiteSpace(pair.Item.TerminalSerial))
            .Select(pair => new ParsedRemoteTransaction(pair.Item, DateTime.SpecifyKind(pair.Parsed!.Value, DateTimeKind.Unspecified)))
            .ToList();
        if (valid.Count == 0) return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var serials = valid.Select(item => item.Item.TerminalSerial.Trim().ToUpperInvariant()).Distinct().ToList();
        var devices = await db.BiometricDevices.Where(item => serials.Contains(item.SerialNumber) && item.IsActive)
            .ToDictionaryAsync(item => item.SerialNumber, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var employeeCodes = valid.Select(item => item.Item.EmployeeCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var employees = await db.Employees.Where(item => employeeCodes.Contains(item.EmployeeCode))
            .ToDictionaryAsync(item => item.EmployeeCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var deviceIds = devices.Values.Select(item => item.Id).ToList();
        var mappings = await db.EmployeeDeviceMappings.Where(item => deviceIds.Contains(item.BiometricDeviceId) && item.IsActive)
            .ToListAsync(cancellationToken);
        var mappingKeys = mappings.Select(item => $"{item.BiometricDeviceId}|{item.DeviceUserId}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in valid)
        {
            if (!devices.TryGetValue(item.Item.TerminalSerial.Trim(), out var device) || !employees.TryGetValue(item.Item.EmployeeCode.Trim(), out var employee)) continue;
            var key = $"{device.Id}|{item.Item.EmployeeCode.Trim()}";
            if (mappingKeys.Add(key)) db.EmployeeDeviceMappings.Add(new EmployeeDeviceMapping { BiometricDeviceId = device.Id, EmployeeId = employee.Id, DeviceUserId = item.Item.EmployeeCode.Trim(), IsActive = true });
        }
        await db.SaveChangesAsync(cancellationToken);

        var hashes = valid.Where(item => devices.ContainsKey(item.Item.TerminalSerial.Trim())).Select(item => ComputeHash(devices[item.Item.TerminalSerial.Trim()].Id, item.Item.Id)).ToList();
        var existingHashList = await db.AttendanceLogs.Where(item => hashes.Contains(item.UniqueHash)).Select(item => item.UniqueHash).ToListAsync(cancellationToken);
        var existingHashes = existingHashList.ToHashSet(StringComparer.Ordinal);
        var mappingLookup = await db.EmployeeDeviceMappings.AsNoTracking().Where(item => deviceIds.Contains(item.BiometricDeviceId) && item.IsActive)
            .ToDictionaryAsync(item => $"{item.BiometricDeviceId}|{item.DeviceUserId}", StringComparer.OrdinalIgnoreCase, cancellationToken);
        var added = 0;
        foreach (var parsed in valid)
        {
            if (!devices.TryGetValue(parsed.Item.TerminalSerial.Trim(), out var device)) continue;
            var hash = ComputeHash(device.Id, parsed.Item.Id);
            if (!existingHashes.Add(hash)) continue;
            mappingLookup.TryGetValue($"{device.Id}|{parsed.Item.EmployeeCode.Trim()}", out var mapping);
            db.AttendanceLogs.Add(new AttendanceLog
            {
                BiometricDeviceId = device.Id,
                EmployeeId = mapping?.EmployeeId,
                DeviceUserId = parsed.Item.EmployeeCode.Trim(),
                PunchTime = parsed.PunchTime,
                PunchState = Limit(parsed.Item.PunchState, 30),
                VerificationMode = parsed.Item.VerifyType.ToString(CultureInfo.InvariantCulture),
                WorkCode = Limit(parsed.Item.WorkCode, 50),
                UniqueHash = hash,
                RawPayload = $"REMOTE|{parsed.Item.Id}|{parsed.Item.TerminalSerial}|{parsed.Item.EmployeeCode}|{parsed.Item.PunchTime}",
                SourceIpAddress = new Uri(_options.BaseUrl).Host,
                ReceivedAtUtc = DateTime.UtcNow
            });
            added++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task<ImportCheckpoint?> LoadCheckpointAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_checkpointPath)) return null;
        await using var stream = File.OpenRead(_checkpointPath);
        return await JsonSerializer.DeserializeAsync<ImportCheckpoint>(stream, JsonOptions, cancellationToken);
    }

    private async Task SaveCheckpointAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var temporaryPath = _checkpointPath + ".tmp";
        await using (var stream = File.Create(temporaryPath)) await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _checkpointPath, true);
    }

    private static Uri? ValidateNextUri(Uri baseUri, string? next)
    {
        if (string.IsNullOrWhiteSpace(next)) return null;
        var uri = new Uri(next, UriKind.Absolute);
        if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) || !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != baseUri.Port)
            throw new InvalidOperationException("Remote API returned an unsafe pagination URL.");
        return uri;
    }

    private static bool TryParseDate(string? value, out DateTime result) => DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    private static string ComputeHash(int deviceId, long transactionId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"REMOTE|{deviceId}|{transactionId}")));
    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, length)];

    private sealed record ParsedRemoteTransaction(RemoteTransaction Item, DateTime PunchTime);
    private sealed record ImportCheckpoint(DateTime PunchTime, long TransactionId);
    private sealed record ApiPage<T>([property: JsonPropertyName("next")] string? Next, [property: JsonPropertyName("data")] List<T> Data);
    private sealed record RemoteArea([property: JsonPropertyName("area_name")] string? Name);
    private sealed record RemoteTerminal(
        [property: JsonPropertyName("sn")] string SerialNumber,
        [property: JsonPropertyName("alias")] string? Alias,
        [property: JsonPropertyName("terminal_name")] string? TerminalName,
        [property: JsonPropertyName("fw_ver")] string? FirmwareVersion,
        [property: JsonPropertyName("area_name")] string? AreaName,
        [property: JsonPropertyName("area")] RemoteArea? Area);
    private sealed record RemoteTransaction(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("emp_code")] string EmployeeCode,
        [property: JsonPropertyName("punch_time")] string PunchTime,
        [property: JsonPropertyName("punch_state")] string? PunchState,
        [property: JsonPropertyName("verify_type")] int VerifyType,
        [property: JsonPropertyName("work_code")] string? WorkCode,
        [property: JsonPropertyName("terminal_sn")] string TerminalSerial);
}
