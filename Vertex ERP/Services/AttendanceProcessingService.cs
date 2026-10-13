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
        var activeEmployeeIds = employees.Select(employee => employee.Id).ToHashSet();
        // Only the current active roster belongs on this screen and its exports.
        logs = logs.Where(log => log.EmployeeId.HasValue && activeEmployeeIds.Contains(log.EmployeeId.Value)).ToList();
        var approvedLeaveIds = await _repository.GetApprovedLeaveEmployeeIdsAsync(date, cancellationToken);
        // Use the source identifier verbatim. Never derive it by stripping an ERP prefix.
        var enrollments = await _repository.GetEnrollmentsAsync(cancellationToken);
        var mappings = await _repository.GetActiveMappingsAsync(cancellationToken);
        var registeredCodes = enrollments.Select(x => x.EnrollCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registeredEmployees = enrollments.Where(x => x.EmployeeId.HasValue).Select(x => x.EmployeeId!.Value).ToHashSet();
        // Legacy device links are valid even before they enter the WDMS registry.
        // A registry review or competing owner must never be bypassed by this fallback.
        var legacyIdentities = mappings.Where(x => x.IsActive)
            .GroupBy(x => x.DeviceUserId.Trim())
            .Where(g => g.Key.Length > 0 && g.Key.All(c => c >= '0' && c <= '9')
                && !registeredCodes.Contains(g.Key) && g.Select(x => x.EmployeeId).Distinct().Count() == 1)
            .Select(g => new { Code = g.Key, EmployeeId = g.First().EmployeeId })
            .GroupBy(x => x.EmployeeId).Where(g => g.Count() == 1 && !registeredEmployees.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.Single().Code);
        var identities = enrollments.Where(x => x.Status == "Linked" && x.EmployeeId.HasValue
                && x.EnrollCode.Length > 0 && x.EnrollCode.All(c => c >= '0' && c <= '9'))
            .GroupBy(x => x.EmployeeId!.Value).Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single());
        (string Code, string Name) Identity(Employee? employee)
        {
            if (employee != null && identities.TryGetValue(employee.Id, out var source))
                return (source.EnrollCode, employee.FullName);
            if (employee != null && legacyIdentities.TryGetValue(employee.Id, out var linkedCode))
                return (linkedCode, employee.FullName);
            return (string.Empty, employee?.FullName ?? "Unmapped employee");
        }
        var startTime = TimeOnly.TryParse(_options.WorkDayStart, out var parsedStart) ? parsedStart : new TimeOnly(9, 30);
        var records = logs.GroupBy(log => log.EmployeeId.HasValue ? $"employee:{log.EmployeeId.Value}" : $"device:{log.BiometricDeviceId}:{log.DeviceUserId}").Select(group =>
        {
            var firstLog = group.First(); var employee = firstLog.Employee;
            var identity = Identity(employee);
            var paired = AttendanceRules.PairPunches(group.Select(log => (log.PunchTime, log.PunchState)));
            var evaluation = AttendanceRules.EvaluateDay(paired.CheckIn, paired.CheckOut, date, DateOnly.FromDateTime(DateTime.Today), startTime);
            return new DailyAttendanceViewModel { EmployeeId = employee?.Id ?? 0, EmpId = identity.Code, EmployeeName = identity.Name, Department = FormatDisplayText(employee?.Department ?? "Unmapped"), Date = date, CheckIn = paired.CheckIn.HasValue ? TimeOnly.FromDateTime(paired.CheckIn.Value) : null, CheckOut = paired.CheckOut.HasValue ? TimeOnly.FromDateTime(paired.CheckOut.Value) : null, WorkingHours = evaluation.Hours, PunchCount = group.Count(), Status = evaluation.Status, IsLate = evaluation.IsLate, Remark = evaluation.Remark,
                SiteCheckIns = FormatSitePunches(group, "Check In"), SiteCheckOuts = FormatSitePunches(group, "Check Out") };
        }).ToList();
        var punchedEmployeeIds = records.Where(record => record.EmployeeId > 0).Select(record => record.EmployeeId).ToHashSet();
        records.AddRange(employees.Where(employee => !punchedEmployeeIds.Contains(employee.Id)).Select(employee =>
            new DailyAttendanceViewModel
            {
                EmployeeId = employee.Id,
                EmpId = Identity(employee).Code,
                EmployeeName = Identity(employee).Name,
                Department = FormatDisplayText(employee.Department),
                Date = date,
                CheckIn = null,
                CheckOut = null,
                WorkingHours = TimeSpan.Zero,
                PunchCount = 0,
                Status = AttendanceRules.NoPunchStatus(date, approvedLeaveIds.Contains(employee.Id)), Remark = AttendanceRules.HolidayName(date) ?? (approvedLeaveIds.Contains(employee.Id) ? "Approved leave" : "No punches recorded")
            }));
        var presentCount = records.Count(record => record.CheckIn.HasValue);
        var lateCount = records.Count(record => record.IsLate);
        var absentCount = records.Count(record => record.Status == "Absent");
        var leaveCount = records.Count(record => record.Status == "Leave");
        var incompleteCount = records.Count(record => record.Status == "Incomplete Punch");
        IEnumerable<DailyAttendanceViewModel> filtered = records;
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(record => record.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase) || record.EmpId.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(department)) filtered = filtered.Where(record => string.Equals(record.Department, department, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) filtered = filtered.Where(record => string.Equals(status, "Present", StringComparison.OrdinalIgnoreCase) ? record.CheckIn.HasValue : string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase));
        _logger.LogDebug("Built attendance for {Date}: {Punches} punches, {Employees} employees", date, logs.Count, records.Count);
        return new AttendancePageViewModel { Records = filtered.OrderBy(record => record.EmployeeName).ToList(), Departments = employees.Select(employee => FormatDisplayText(employee.Department)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(), PresentCount = presentCount, AbsentCount = absentCount, LeaveCount = leaveCount, LateCount = lateCount, IncompleteCount = incompleteCount, SearchQuery = search, Department = department, FilterDate = date, Status = status };
    }

    private static string FormatSitePunches(IEnumerable<AttendanceLog> logs, string action) =>
        string.Join("\n", logs.Where(log =>
                (log.VerificationMode == AttendanceRules.FieldVerificationMode || !string.IsNullOrWhiteSpace(log.FieldSiteName))
                && AttendanceRules.NormalizePunchAction(log.PunchState) == action)
            .OrderBy(log => log.PunchTime).Select(log =>
                $"{log.PunchTime:dd-MMM-yyyy hh:mm tt} | {log.FieldSiteName ?? "Site not recorded"} | " +
                (log.Latitude.HasValue && log.Longitude.HasValue
                    ? FormattableString.Invariant($"https://www.google.com/maps?q={log.Latitude},{log.Longitude}")
                    : "GPS not recorded")));

    private static string FormatDisplayText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "Unassigned";

        var spaced = Regex.Replace(trimmed, @"\s*&\s*", " & ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }
}
