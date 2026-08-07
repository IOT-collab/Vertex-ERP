using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class BiometricDevice
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string SerialNumber { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Model { get; set; } = string.Empty;
    [MaxLength(100)] public string? BranchCode { get; set; }
    [MaxLength(255)] public string? ServerAddress { get; set; }
    public int ServerPort { get; set; } = 8082;
    [Required, MaxLength(20)] public string CommunicationMode { get; set; } = "ADMS";
    [MaxLength(50)] public string? FirmwareVersion { get; set; }
    [MaxLength(45)] public string? LastKnownIpAddress { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSeenUtc { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<AttendanceLog> AttendanceLogs { get; set; } = new List<AttendanceLog>();
    public ICollection<EmployeeDeviceMapping> EmployeeMappings { get; set; } = new List<EmployeeDeviceMapping>();
}
