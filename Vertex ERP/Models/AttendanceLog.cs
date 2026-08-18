using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class AttendanceLog
{
    public long Id { get; set; }
    public int BiometricDeviceId { get; set; }
    public BiometricDevice BiometricDevice { get; set; } = null!;
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    [Required, MaxLength(50)] public string DeviceUserId { get; set; } = string.Empty;
    public DateTime PunchTime { get; set; }
    [MaxLength(30)] public string? PunchState { get; set; }
    [MaxLength(30)] public string? VerificationMode { get; set; }
    [MaxLength(50)] public string? WorkCode { get; set; }
    [Required, MaxLength(64)] public string UniqueHash { get; set; } = string.Empty;
    [Required] public string RawPayload { get; set; } = string.Empty;
    [MaxLength(45)] public string? SourceIpAddress { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AccuracyMetres { get; set; }
    [MaxLength(300)] public string? SelfiePath { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
