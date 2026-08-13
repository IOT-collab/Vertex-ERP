using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class BiometricPunchBatchRequest
{
    [Required, StringLength(100)] public string DeviceSerialNumber { get; set; } = string.Empty;
    [Required, MinLength(1), MaxLength(1000)] public List<BiometricPunchRequest> Punches { get; set; } = new();
}

public sealed class BiometricPunchRequest
{
    [Required, StringLength(50)] public string DeviceUserId { get; set; } = string.Empty;
    public DateTime PunchTime { get; set; }
    [StringLength(30)] public string? PunchState { get; set; }
    [StringLength(30)] public string? VerificationMode { get; set; }
    [StringLength(50)] public string? WorkCode { get; set; }
    [StringLength(100)] public string? EventId { get; set; }
}

public sealed record BiometricPunchBatchResponse(bool Accepted, int Received, int Saved, int Unmapped, string Message);
