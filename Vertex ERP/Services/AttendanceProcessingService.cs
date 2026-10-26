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
        var from = date.ToDateTime(TimeOnly.MinValue); var to = from.AddDays(2);
        var logs = await _repository.GetAttendanceLogsAsync(from, to, cancellationToken);
        var approvedLeaveIds = await _repository.GetApprovedLeaveEmployeeIdsAsync(date, cancellationToken);
        var enrollments = await _repository.GetEnrollmentsAsync(cancellationToken);
        // Source membership and names come only from the last complete WDMS directory sync.
        var roster = enrollments.Where(x => x.LastSeenUtc.HasValue).ToList();
        var uniqueLinks = roster.Where(x => x.Status == "Linked" && x.EmployeeId.HasValue)
            .GroupBy(x => x.EmployeeId!.Value).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().EnrollCode);
        var startTime = TimeOnly.TryParse(_options.WorkDayStart, out var parsedStart) ? parsedStart : new TimeOnly(9, 30);
        var records = roster.Select(source =>
        {
            var employee = source.Status == "Linked" ? source.Employee : null;
            var shiftStart = employee?.ShiftStartTime ?? startTime;
            var shiftEnd = employee?.ShiftEndTime ?? new TimeOnly(18, 30);
            var window = AttendanceRules.PunchWindow(date, shiftStart, shiftEnd);
            var punches = logs.Where(log =>
                log.PunchTime >= window.From && log.PunchTime < window.To && (
                // Biometric punches belong to their source enroll code even before ERP linking.
                (log.VerificationMode != "Manual Approved" && log.VerificationMode != AttendanceRules.FieldVerificationMode
                    && string.Equals(log.DeviceUserId.Trim(), source.EnrollCode, StringComparison.OrdinalIgnoreCase))
                // Manual/field attendance may use an ERP code: require a unique verified link.
                || ((log.VerificationMode == "Manual Approved" || log.VerificationMode == AttendanceRules.FieldVerificationMode)
                    && log.EmployeeId.HasValue && uniqueLinks.TryGetValue(log.EmployeeId.Value, out var code)
                    && code == source.EnrollCode))).ToList();
            var paired = AttendanceRules.PairPunches(punches.Select(log => (log.PunchTime, log.PunchState)));
            var evaluation = AttendanceRules.EvaluateDay(paired.CheckIn, paired.CheckOut, date, DateOnly.FromDateTime(DateTime.Today), shiftStart);
            var onLeave = employee != null && approvedLeaveIds.Contains(employee.Id);
            return new DailyAttendanceViewModel
            {
                EmployeeId = employee?.Id ?? 0, EmpId = source.EnrollCode,
                EmployeeName = string.IsNullOrWhiteSpace(source.WdmsName) ? "Name unavailable in WDMS" : source.WdmsName,
                Department = FormatDisplayText(employee?.Department), Date = date,
                CheckIn = paired.CheckIn.HasValue ? TimeOnly.FromDateTime(paired.CheckIn.Value) : null,
                CheckOut = paired.CheckOut.HasValue ? TimeOnly.FromDateTime(paired.CheckOut.Value) : null,
                WorkingHours = evaluation.Hours, PunchCount = punches.Count,
                OvertimeEnabled = employee?.OvertimeEnabled ?? false, ShiftStartTime = shiftStart, ShiftEndTime = shiftEnd,
                Status = punches.Count == 0 ? AttendanceRules.NoPunchStatus(date, onLeave) : evaluation.Status,
                IsLate = evaluation.IsLate,
                Remark = punches.Count == 0 ? AttendanceRules.HolidayName(date) ?? (onLeave ? "Approved leave" : "No punches recorded") : evaluation.Remark,
                SiteCheckIns = FormatSitePunches(punches, "Check In"), SiteCheckOuts = FormatSitePunches(punches, "Check Out")
            };
        }).ToList();
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
        return new AttendancePageViewModel { Records = filtered.OrderBy(record => record.EmployeeName).ToList(), Departments = records.Select(record => record.Department).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(), PresentCount = presentCount, AbsentCount = absentCount, LeaveCount = leaveCount, LateCount = lateCount, IncompleteCount = incompleteCount, SearchQuery = search, Department = department, FilterDate = date, Status = status };
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
