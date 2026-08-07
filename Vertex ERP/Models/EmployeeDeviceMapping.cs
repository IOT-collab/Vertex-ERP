using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class EmployeeDeviceMapping
{
    public int Id { get; set; }
    public int BiometricDeviceId { get; set; }
    public BiometricDevice BiometricDevice { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(50)] public string DeviceUserId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
