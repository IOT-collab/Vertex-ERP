using VertexERP.Models;

namespace VertexERP.Repositories;

public interface IBiometricRepository
{
    Task<IReadOnlyList<BiometricDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<BiometricDevice?> GetDeviceAsync(int id, CancellationToken cancellationToken = default);
    Task<BiometricDevice?> GetDeviceBySerialAsync(string serialNumber, CancellationToken cancellationToken = default);
    Task AddDeviceAsync(BiometricDevice device, CancellationToken cancellationToken = default);
    void RemoveDevice(BiometricDevice device);
    Task<EmployeeDeviceMapping?> GetMappingAsync(int deviceId, string deviceUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeDeviceMapping>> GetMappingsAsync(int deviceId, CancellationToken cancellationToken = default);
    Task AddOrUpdateMappingAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default);
    Task<bool> AttendanceHashExistsAsync(string hash, CancellationToken cancellationToken = default);
    Task AddAttendanceLogsAsync(IEnumerable<AttendanceLog> logs, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceLog>> GetAttendanceLogsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken = default);
    Task<bool> DeviceHasAttendanceAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
