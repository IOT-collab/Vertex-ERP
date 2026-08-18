using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class BiometricDeviceFormViewModel
{
    public int Id { get; set; }
    [Required, StringLength(100), Display(Name = "Device Name")] public string Name { get; set; } = string.Empty;
    [Required, StringLength(100), Display(Name = "Serial Number")] public string SerialNumber { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Model { get; set; } = "ZKTeco K40 Pro";
    [StringLength(100), Display(Name = "Branch Code")] public string? BranchCode { get; set; }
    [StringLength(255), Display(Name = "Server Address")] public string? ServerAddress { get; set; }
    [Range(1, 65535), Display(Name = "Server Port")] public int ServerPort { get; set; } = 8082;
    [Required, StringLength(20), Display(Name = "Communication Mode")] public string CommunicationMode { get; set; } = "ADMS";
    [StringLength(50), Display(Name = "Firmware Version")] public string? FirmwareVersion { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(500)] public string? Notes { get; set; }
}

public class BiometricDeviceListItemViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BranchCode { get; init; } = "—";
    public string Endpoint { get; init; } = "—";
    public bool IsActive { get; init; }
    public bool IsOnline { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public DateTime? LastSyncUtc { get; init; }
    public int MappingCount { get; init; }
}

public class EmployeeDeviceMappingViewModel
{
    public int DeviceId { get; set; }
    [Required] public int? EmployeeId { get; set; }
    [Required, StringLength(50), Display(Name = "Device User ID")] public string DeviceUserId { get; set; } = string.Empty;
}

public sealed class UnmappedDeviceUserViewModel
{
    public string DeviceUserId { get; init; } = string.Empty;
    public int PunchCount { get; init; }
    public DateTime LastPunch { get; init; }
}

public class AttendancePageViewModel
{
    public IReadOnlyList<DailyAttendanceViewModel> Records { get; init; } = Array.Empty<DailyAttendanceViewModel>();
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();
    public int PresentCount { get; init; }
    public int AbsentCount { get; init; }
    public int LeaveCount { get; init; }
    public int LateCount { get; init; }
    public string? SearchQuery { get; init; }
    public string? Department { get; init; }
    public DateOnly FilterDate { get; init; }
    public string? Status { get; init; }
}

public class DailyAttendanceViewModel
{
    public int EmployeeId { get; init; }
    public string EmpId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public TimeOnly? CheckIn { get; init; }
    public TimeOnly? CheckOut { get; init; }
    public TimeSpan WorkingHours { get; init; }
    public int PunchCount { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class ManualAttendanceViewModel
{
    public string? Department { get; set; }
    public int? EmployeeId { get; set; }
    public DateOnly AttendanceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<string> Departments { get; set; } = Array.Empty<string>();
    public IReadOnlyList<Employee> Employees { get; set; } = Array.Empty<Employee>();
    public bool IsManagerView { get; set; }
}
