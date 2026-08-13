using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VertexERP.Models;
using VertexERP.Repositories;

namespace VertexERP.Services;

public sealed class AttendanceSyncService : IAttendanceSyncService
{
    private static readonly string[] TimestampFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss"];
    private readonly IBiometricRepository _repository;
    private readonly ILogger<AttendanceSyncService> _logger;

    public AttendanceSyncService(IBiometricRepository repository, ILogger<AttendanceSyncService> logger) { _repository = repository; _logger = logger; }

    public async Task<bool> RegisterHeartbeatAsync(string serialNumber, string? sourceIp, CancellationToken cancellationToken = default)
    {
        var device = await FindActiveDeviceAsync(serialNumber, cancellationToken); if (device is null) return false;
        Touch(device, sourceIp, false); await _repository.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<AttendanceSyncResult> ReceiveAsync(string serialNumber, string rawPayload, string? sourceIp, CancellationToken cancellationToken = default)
    {
        var device = await FindActiveDeviceAsync(serialNumber, cancellationToken);
        if (device is null) { _logger.LogWarning("Rejected ADMS payload from unknown or inactive serial {SerialNumber}", serialNumber); return new(false, 0, 0, 0, "Unknown device"); }
        var rows = rawPayload.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var logs = new List<AttendanceLog>(); var unmapped = 0;
        foreach (var row in rows)
        {
            if (row.Length > 2048) continue;
            if (!TryParsePunch(row, out var punch)) continue;
            if (punch.PunchTime < DateTime.Today.AddYears(-2) || punch.PunchTime > DateTime.Now.AddDays(1)) continue;
            var hash = ComputeHash(device.Id, punch.DeviceUserId, punch.PunchTime, row);
            if (await _repository.AttendanceHashExistsAsync(hash, cancellationToken) ||
                await _repository.AttendancePunchExistsAsync(device.Id, punch.DeviceUserId, punch.PunchTime, cancellationToken) ||
                logs.Any(log => log.UniqueHash == hash || (log.DeviceUserId == punch.DeviceUserId && log.PunchTime == punch.PunchTime))) continue;
            var mapping = await _repository.GetMappingAsync(device.Id, punch.DeviceUserId, cancellationToken);
            if (mapping is null) unmapped++;
            logs.Add(new AttendanceLog { BiometricDeviceId = device.Id, EmployeeId = mapping?.EmployeeId, DeviceUserId = punch.DeviceUserId, PunchTime = punch.PunchTime, PunchState = punch.PunchState, VerificationMode = punch.VerificationMode, WorkCode = punch.WorkCode, UniqueHash = hash, RawPayload = row, SourceIpAddress = sourceIp, ReceivedAtUtc = DateTime.UtcNow });
        }
        if (logs.Count > 0) await _repository.AddAttendanceLogsAsync(logs, cancellationToken);
        Touch(device, sourceIp, logs.Count > 0); await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("ADMS sync from {SerialNumber}: received {Received}, saved {Saved}, unmapped {Unmapped}", device.SerialNumber, rows.Length, logs.Count, unmapped);
        return new(true, rows.Length, logs.Count, unmapped, "OK");
    }

    public async Task<AttendanceSyncResult> ReceiveNormalizedAsync(string serialNumber, IReadOnlyList<NormalizedBiometricPunch> punches, string? sourceIp, CancellationToken cancellationToken = default)
    {
        var device = await FindActiveDeviceAsync(serialNumber, cancellationToken);
        if (device is null) return new(false, punches.Count, 0, 0, "Unknown or inactive device");
        var logs = new List<AttendanceLog>(); var unmapped = 0;
        foreach (var punch in punches.Take(1000))
        {
            var userId = punch.DeviceUserId.Trim();
            var time = DateTime.SpecifyKind(punch.PunchTime, DateTimeKind.Unspecified);
            if (userId.Length is 0 or > 50 || time < DateTime.Today.AddYears(-2) || time > DateTime.Now.AddDays(1)) continue;
            var raw = $"GENERIC|{userId}|{time:O}|{punch.PunchState}|{punch.VerificationMode}|{punch.WorkCode}|{punch.EventId}";
            var hash = ComputeHash(device.Id, userId, time, raw);
            if (await _repository.AttendanceHashExistsAsync(hash, cancellationToken) ||
                await _repository.AttendancePunchExistsAsync(device.Id, userId, time, cancellationToken) ||
                logs.Any(log => log.UniqueHash == hash || (log.DeviceUserId == userId && log.PunchTime == time))) continue;
            var mapping = await _repository.GetMappingAsync(device.Id, userId, cancellationToken);
            if (mapping is null) unmapped++;
            logs.Add(new AttendanceLog { BiometricDeviceId = device.Id, EmployeeId = mapping?.EmployeeId, DeviceUserId = userId, PunchTime = time, PunchState = Limit(punch.PunchState, 30), VerificationMode = Limit(punch.VerificationMode, 30), WorkCode = Limit(punch.WorkCode, 50), UniqueHash = hash, RawPayload = raw, SourceIpAddress = sourceIp, ReceivedAtUtc = DateTime.UtcNow });
        }
        if (logs.Count > 0) await _repository.AddAttendanceLogsAsync(logs, cancellationToken);
        Touch(device, sourceIp, logs.Count > 0); await _repository.SaveChangesAsync(cancellationToken);
        return new(true, punches.Count, logs.Count, unmapped, "OK");
    }

    private async Task<BiometricDevice?> FindActiveDeviceAsync(string serial, CancellationToken cancellationToken) => string.IsNullOrWhiteSpace(serial) ? null : (await _repository.GetDeviceBySerialAsync(serial.Trim().ToUpperInvariant(), cancellationToken)) is { IsActive: true } device ? device : null;
    private static void Touch(BiometricDevice device, string? sourceIp, bool synced) { device.LastSeenUtc = DateTime.UtcNow; device.LastKnownIpAddress = sourceIp; if (synced) device.LastSyncUtc = DateTime.UtcNow; }

    private static bool TryParsePunch(string row, out ParsedPunch punch)
    {
        punch = default;
        var value = row.StartsWith("ATTLOG", StringComparison.OrdinalIgnoreCase) && row.Contains('=') ? row[(row.IndexOf('=') + 1)..] : row;
        var fields = value.Split('\t');
        if (fields.Length < 2) fields = value.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length < 2) return false;
        var userId = fields[0].StartsWith("PIN=", StringComparison.OrdinalIgnoreCase) ? fields[0][4..] : fields[0];
        var timestampText = fields[1].StartsWith("DateTime=", StringComparison.OrdinalIgnoreCase) ? fields[1][9..] : fields[1];
        if (string.IsNullOrWhiteSpace(userId) || !DateTime.TryParseExact(timestampText.Trim(), TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return false;
        userId = userId.Trim();
        if (userId.Length > 50) return false;
        punch = new ParsedPunch(userId, DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified), Limit(fields.ElementAtOrDefault(2), 30), Limit(fields.ElementAtOrDefault(3), 30), Limit(fields.ElementAtOrDefault(4), 50)); return true;
    }
    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, length)];
    private static string ComputeHash(int deviceId, string userId, DateTime time, string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{deviceId}|{userId}|{time:O}|{raw}")));
    private readonly record struct ParsedPunch(string DeviceUserId, DateTime PunchTime, string? PunchState, string? VerificationMode, string? WorkCode);
}
