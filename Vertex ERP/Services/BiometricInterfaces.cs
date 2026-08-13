using VertexERP.Models;

namespace VertexERP.Services;

public interface IBiometricDeviceService
{
    Task<IReadOnlyList<BiometricDeviceListItemViewModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BiometricDevice?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(BiometricDeviceFormViewModel model, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(BiometricDeviceFormViewModel model, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> TestConnectionAsync(int id, CancellationToken cancellationToken = default);
}

public interface IAttendanceSyncService
{
    Task<AttendanceSyncResult> ReceiveAsync(string serialNumber, string rawPayload, string? sourceIp, CancellationToken cancellationToken = default);
    Task<AttendanceSyncResult> ReceiveNormalizedAsync(string serialNumber, IReadOnlyList<NormalizedBiometricPunch> punches, string? sourceIp, CancellationToken cancellationToken = default);
    Task<bool> RegisterHeartbeatAsync(string serialNumber, string? sourceIp, CancellationToken cancellationToken = default);
}

public interface IAttendanceProcessingService
{
    Task<AttendancePageViewModel> GetDailyAttendanceAsync(DateOnly date, string? search, string? department, string? status, CancellationToken cancellationToken = default);
}

public sealed record AttendanceSyncResult(bool Accepted, int Received, int Saved, int Unmapped, string Message);
public sealed record NormalizedBiometricPunch(string DeviceUserId, DateTime PunchTime, string? PunchState, string? VerificationMode, string? WorkCode, string? EventId);

public sealed class AttendanceOptions
{
    public const string SectionName = "AttendanceProcessing";
    public string WorkDayStart { get; set; } = "09:30";
    public int OnlineThresholdMinutes { get; set; } = 10;
}
