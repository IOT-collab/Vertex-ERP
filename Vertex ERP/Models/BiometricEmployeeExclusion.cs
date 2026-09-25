using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

// Preserve HR's deletion decision when old punches are replayed by a device.
public sealed class BiometricEmployeeExclusion
{
    [Key, MaxLength(50)] public string DeviceUserCode { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
}
