using Microsoft.Extensions.Options;
using VertexERP.Models;
using VertexERP.Repositories;

namespace VertexERP.Services;

public sealed class AttendanceProcessingService : IAttendanceProcessingService
{
    private readonly IBiometricRepository _repository;
    private readonly AttendanceOptions _options;
    private readonly ILogger<AttendanceProcessingService> _logger;

    public AttendanceProcessingService(IBiometricRepository repository, IOptions<AttendanceOptions> options, ILogger<AttendanceProcessingService> logger)
    { _repository = repository; _options = options.Value; _logger = logger; }

    public async Task<AttendancePageViewModel> GetDailyAttendanceAsync(DateOnly date, string? search, string? department, string? status, CancellationToken cancellationToken = default)
    {
        var from = date.ToDateTime(TimeOnly.MinValue); var to = from.AddDays(1);
        var logs = await _repository.GetAttendanceLogsAsync(from, to, cancellationToken);
        var employees = await _repository.GetActiveEmployeesAsync(cancellationToken);
        var startTime = TimeOnly.TryParse(_options.WorkDayStart, out var parsedStart) ? parsedStart : new TimeOnly(9, 30);
        var records = logs.GroupBy(log => log.EmployeeId.HasValue ? $"employee:{log.EmployeeId.Value}" : $"device:{log.BiometricDeviceId}:{log.DeviceUserId}").Select(group =>
        {
            var firstLog = group.First(); var first = group.Min(log => log.PunchTime); var last = group.Max(log => log.PunchTime); var employee = firstLog.Employee;
            var late = TimeOnly.FromDateTime(first) > startTime;
            return new DailyAttendanceViewModel { EmployeeId = employee?.Id ?? 0, EmpId = employee?.EmployeeCode ?? $"BIO-{firstLog.DeviceUserId}", EmployeeName = employee?.FullName ?? $"Unmapped User {firstLog.DeviceUserId}", Department = employee?.Department ?? "Unmapped", Date = date, CheckIn = TimeOnly.FromDateTime(first), CheckOut = group.Count() > 1 ? TimeOnly.FromDateTime(last) : null, WorkingHours = group.Count() > 1 ? last - first : TimeSpan.Zero, PunchCount = group.Count(), Status = employee is null ? "Unmapped" : late ? "Late" : "Present" };
        }).ToList();
        var presentCount = records.Count; var lateCount = records.Count(record => record.Status == "Late");
        IEnumerable<DailyAttendanceViewModel> filtered = records;
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(record => record.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase) || record.EmpId.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(department)) filtered = filtered.Where(record => string.Equals(record.Department, department, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) filtered = filtered.Where(record => string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase));
        _logger.LogDebug("Built attendance for {Date}: {Punches} punches, {Employees} employees", date, logs.Count, records.Count);
        return new AttendancePageViewModel { Records = filtered.OrderBy(record => record.EmployeeName).ToList(), Departments = employees.Select(employee => employee.Department).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(), PresentCount = presentCount, AbsentCount = Math.Max(0, employees.Count - presentCount), LeaveCount = 0, LateCount = lateCount, SearchQuery = search, Department = department, FilterDate = date, Status = status };
    }
}
