using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Repositories;

public sealed class BiometricRepository : IBiometricRepository
{
    private readonly ApplicationDbContext _db;
    public BiometricRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<BiometricDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        await _db.BiometricDevices.AsNoTracking().Include(device => device.EmployeeMappings).OrderBy(device => device.Name).ToListAsync(cancellationToken);

    public Task<BiometricDevice?> GetDeviceAsync(int id, CancellationToken cancellationToken = default) =>
        _db.BiometricDevices.Include(device => device.EmployeeMappings).ThenInclude(mapping => mapping.Employee).SingleOrDefaultAsync(device => device.Id == id, cancellationToken);

    public Task<BiometricDevice?> GetDeviceBySerialAsync(string serialNumber, CancellationToken cancellationToken = default) =>
        _db.BiometricDevices.SingleOrDefaultAsync(device => device.SerialNumber == serialNumber, cancellationToken);

    public async Task AddDeviceAsync(BiometricDevice device, CancellationToken cancellationToken = default) => await _db.BiometricDevices.AddAsync(device, cancellationToken);
    public void RemoveDevice(BiometricDevice device) => _db.BiometricDevices.Remove(device);
    public Task<EmployeeDeviceMapping?> GetMappingAsync(int deviceId, string deviceUserId, CancellationToken cancellationToken = default) =>
        _db.EmployeeDeviceMappings.SingleOrDefaultAsync(mapping => mapping.BiometricDeviceId == deviceId && mapping.DeviceUserId == deviceUserId && mapping.IsActive, cancellationToken);
    public async Task<IReadOnlyList<EmployeeDeviceMapping>> GetMappingsAsync(int deviceId, CancellationToken cancellationToken = default) =>
        await _db.EmployeeDeviceMappings.AsNoTracking().Include(mapping => mapping.Employee).Where(mapping => mapping.BiometricDeviceId == deviceId).OrderBy(mapping => mapping.DeviceUserId).ToListAsync(cancellationToken);
    public async Task AddOrUpdateMappingAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default)
    {
        var existing = await _db.EmployeeDeviceMappings.SingleOrDefaultAsync(item => item.BiometricDeviceId == mapping.BiometricDeviceId && (item.DeviceUserId == mapping.DeviceUserId || item.EmployeeId == mapping.EmployeeId), cancellationToken);
        if (existing is null) await _db.EmployeeDeviceMappings.AddAsync(mapping, cancellationToken);
        else { existing.DeviceUserId = mapping.DeviceUserId; existing.EmployeeId = mapping.EmployeeId; existing.IsActive = true; }
    }
    public Task<bool> AttendanceHashExistsAsync(string hash, CancellationToken cancellationToken = default) => _db.AttendanceLogs.AnyAsync(log => log.UniqueHash == hash, cancellationToken);
    public async Task AddAttendanceLogsAsync(IEnumerable<AttendanceLog> logs, CancellationToken cancellationToken = default) => await _db.AttendanceLogs.AddRangeAsync(logs, cancellationToken);
    public async Task<IReadOnlyList<AttendanceLog>> GetAttendanceLogsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        await _db.AttendanceLogs.AsNoTracking().Include(log => log.Employee).Where(log => log.PunchTime >= from && log.PunchTime < to).OrderBy(log => log.PunchTime).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken = default) =>
        await _db.Employees.AsNoTracking().Where(employee => employee.IsActive).OrderBy(employee => employee.FullName).ToListAsync(cancellationToken);
    public Task<bool> DeviceHasAttendanceAsync(int deviceId, CancellationToken cancellationToken = default) => _db.AttendanceLogs.AnyAsync(log => log.BiometricDeviceId == deviceId, cancellationToken);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}
