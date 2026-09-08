using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;
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
        var approvedLeaveEmployeeIds = await _repository.GetApprovedLeaveEmployeeIdsAsync(date, cancellationToken);
        var startTime = TimeOnly.TryParse(_options.WorkDayStart, out var parsedStart) ? parsedStart : new TimeOnly(9, 30);
        var records = logs.GroupBy(log => log.EmployeeId.HasValue ? $"employee:{log.EmployeeId.Value}" : $"device:{log.BiometricDeviceId}:{log.DeviceUserId}").Select(group =>
        {
            var firstLog = group.First(); var employee = firstLog.Employee;
            var paired = AttendanceRules.PairPunches(group.Select(log => (log.PunchTime, log.PunchState)));
            var late = paired.CheckIn.HasValue && TimeOnly.FromDateTime(paired.CheckIn.Value) > startTime;
            var statusValue = employee is null ? "Unmapped" : paired.NeedsReview ? "Needs Review" : !paired.CheckOut.HasValue ? "Incomplete" : late ? "Late" : "Present";
            return new DailyAttendanceViewModel { EmployeeId = employee?.Id ?? 0, EmpId = employee?.EmployeeCode ?? $"BIO-{firstLog.DeviceUserId}", EmployeeName = FormatDisplayText(employee?.FullName ?? $"Unmapped User {firstLog.DeviceUserId}"), Department = FormatDisplayText(employee?.Department ?? "Unmapped"), Date = date, CheckIn = paired.CheckIn.HasValue ? TimeOnly.FromDateTime(paired.CheckIn.Value) : null, CheckOut = paired.CheckOut.HasValue ? TimeOnly.FromDateTime(paired.CheckOut.Value) : null, WorkingHours = paired.CheckIn.HasValue && paired.CheckOut.HasValue ? paired.CheckOut.Value - paired.CheckIn.Value : TimeSpan.Zero, PunchCount = group.Count(), Status = statusValue };
        }).ToList();
        var punchedEmployeeIds = records.Where(record => record.EmployeeId > 0).Select(record => record.EmployeeId).ToHashSet();
        records.AddRange(employees.Where(employee => !punchedEmployeeIds.Contains(employee.Id)).Select(employee =>
            new DailyAttendanceViewModel
            {
                EmployeeId = employee.Id,
                EmpId = employee.EmployeeCode,
                EmployeeName = FormatDisplayText(employee.FullName),
                Department = FormatDisplayText(employee.Department),
                Date = date,
                CheckIn = null,
                CheckOut = null,
                WorkingHours = TimeSpan.Zero,
                PunchCount = 0,
                Status = approvedLeaveEmployeeIds.Contains(employee.Id) ? "On Leave" : AttendanceRules.IsWeeklyOff(date) ? "Sunday Off" : "Absent"
            }));
        var presentCount = records.Count(record => record.Status == "Present");
        var lateCount = records.Count(record => record.Status == "Late");
        var absentCount = records.Count(record => record.Status == "Absent");
        var leaveCount = records.Count(record => record.Status == "On Leave");
        var incompleteCount = records.Count(record => record.Status is "Incomplete" or "Needs Review");
        IEnumerable<DailyAttendanceViewModel> filtered = records;
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(record => record.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase) || record.EmpId.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(department)) filtered = filtered.Where(record => string.Equals(record.Department, department, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) filtered = filtered.Where(record => string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase));
        _logger.LogDebug("Built attendance for {Date}: {Punches} punches, {Employees} employees", date, logs.Count, records.Count);
        return new AttendancePageViewModel { Records = filtered.OrderBy(record => record.EmployeeName).ToList(), Departments = employees.Select(employee => FormatDisplayText(employee.Department)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(), PresentCount = presentCount, AbsentCount = absentCount, LeaveCount = leaveCount, LateCount = lateCount, IncompleteCount = incompleteCount, SearchQuery = search, Department = department, FilterDate = date, Status = status };
    }

    private static string FormatDisplayText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "Unassigned";

        var spaced = Regex.Replace(trimmed, @"\s*&\s*", " & ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }
}
