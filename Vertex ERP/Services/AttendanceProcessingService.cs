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
        return BuildDay(date, search, department, status, logs, approvedLeaveIds, enrollments);
    }

    public async Task<IReadOnlyList<DailyAttendanceViewModel>> GetAttendanceRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // One database snapshot for punches and roster instead of reloading the month for each day.
        var logs = await _repository.GetAttendanceLogsAsync(from.ToDateTime(TimeOnly.MinValue), to.AddDays(2).ToDateTime(TimeOnly.MinValue), cancellationToken);
        var enrollments = await _repository.GetEnrollmentsAsync(cancellationToken);
        var leaves = _repository is BiometricRepository databaseRepository
            ? await databaseRepository.GetApprovedLeavesForRangeAsync(from, to, cancellationToken) : null;
        var records = new List<DailyAttendanceViewModel>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlySet<int> leaveIds = leaves != null
                ? leaves.Where(request => request.FromDate <= day && request.ToDate >= day).Select(request => request.EmployeeId).ToHashSet()
                : await _repository.GetApprovedLeaveEmployeeIdsAsync(day, cancellationToken);
            var dayFrom = day.ToDateTime(TimeOnly.MinValue);
            var dayLogs = logs.Where(log => log.PunchTime >= dayFrom && log.PunchTime < dayFrom.AddDays(2)).ToList();
            records.AddRange(BuildDay(day, null, null, null, dayLogs, leaveIds, enrollments).Records);
        }
        return records;
    }

    private AttendancePageViewModel BuildDay(DateOnly date, string? search, string? department, string? status,
        IReadOnlyList<AttendanceLog> logs, IReadOnlySet<int> approvedLeaveIds, IReadOnlyList<BiometricEnrollment> enrollments)
    {
        // Source membership and names come only from the last complete WDMS directory sync.
        var roster = enrollments.Where(x => x.LastSeenUtc.HasValue).ToList();
        var uniqueLinks = roster.Where(x => x.Status == "Linked" && x.EmployeeId.HasValue)
            .GroupBy(x => x.EmployeeId!.Value).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().EnrollCode);
        var startTime = TimeOnly.TryParse(_options.WorkDayStart, out var parsedStart) ? parsedStart : new TimeOnly(9, 45);
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
            var paired = ManualAttendance.EffectivePunches(punches);
            var evaluation = AttendanceRules.EvaluateDay(paired.CheckIn, paired.CheckOut, date, DateOnly.FromDateTime(DateTime.Today), shiftStart);
            var onLeave = employee != null && approvedLeaveIds.Contains(employee.Id);
            return new DailyAttendanceViewModel
            {
                EmployeeId = employee?.Id ?? 0, EmpId = source.EnrollCode,
                EmployeeName = EmployeeProfileDisplay.Name(employee, source.WdmsName),
                Department = FormatDisplayText(employee?.Department), Date = date,
                CheckIn = paired.CheckIn.HasValue ? TimeOnly.FromDateTime(paired.CheckIn.Value) : null,
                CheckOut = paired.CheckOut.HasValue ? TimeOnly.FromDateTime(paired.CheckOut.Value) : null,
                WorkingHours = evaluation.Hours, PunchCount = punches.Count,
                OvertimeEnabled = employee?.OvertimeEnabled ?? false, ShiftStartTime = shiftStart, ShiftEndTime = shiftEnd,
                Status = punches.Count == 0 ? AttendanceRules.NoPunchStatus(date, onLeave) : evaluation.Status,
                IsLate = evaluation.IsLate,
                Remark = punches.Count == 0 ? AttendanceRules.HolidayName(date) ?? (onLeave ? "Approved leave" : "No punches recorded") : evaluation.Remark + LateDetails(paired.CheckIn, date, shiftStart) + EarlyExit(paired.CheckOut, date, shiftStart, shiftEnd),
                SiteCheckIns = FormatSitePunches(punches, "Check In"), SiteCheckOuts = FormatSitePunches(punches, "Check Out")
            };
        }).ToList();
        // Explicit HR/Admin manual marks must appear even without a WDMS enrollment.
        // Do not synthesize unrelated ERP employees into the biometric roster.
        foreach (var group in logs.Where(x => x.VerificationMode == ManualAttendance.VerificationMode && x.EmployeeId.HasValue && x.Employee != null)
                     .GroupBy(x => x.EmployeeId!.Value).Where(g => records.All(x => x.EmployeeId != g.Key)))
        {
            var employee = group.First().Employee!;
            var window = AttendanceRules.PunchWindow(date, employee.ShiftStartTime, employee.ShiftEndTime);
            var punches = logs.Where(x => x.EmployeeId == employee.Id && x.PunchTime >= window.From && x.PunchTime < window.To).ToList();
            if (!punches.Any(x => x.VerificationMode == ManualAttendance.VerificationMode)) continue;
            var pair = ManualAttendance.EffectivePunches(punches);
            var day = AttendanceRules.EvaluateDay(pair.CheckIn, pair.CheckOut, date, DateOnly.FromDateTime(FieldAttendanceClock.Now), employee.ShiftStartTime);
            records.Add(new DailyAttendanceViewModel { EmployeeId = employee.Id, EmpId = employee.EmployeeCode, EmployeeName = employee.FullName,
                Department = FormatDisplayText(employee.Department), Date = date,
                CheckIn = pair.CheckIn.HasValue ? TimeOnly.FromDateTime(pair.CheckIn.Value) : null,
                CheckOut = pair.CheckOut.HasValue ? TimeOnly.FromDateTime(pair.CheckOut.Value) : null,
                WorkingHours = day.Hours, PunchCount = punches.Count, Status = day.Status, IsLate = day.IsLate,
                ShiftStartTime = employee.ShiftStartTime, ShiftEndTime = employee.ShiftEndTime, OvertimeEnabled = employee.OvertimeEnabled,
                Remark = day.Remark + LateDetails(pair.CheckIn, date, employee.ShiftStartTime) + EarlyExit(pair.CheckOut, date, employee.ShiftStartTime, employee.ShiftEndTime),
                SiteCheckIns = FormatSitePunches(punches, "Check In"), SiteCheckOuts = FormatSitePunches(punches, "Check Out") });
        }
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

    private static string LateDetails(DateTime? checkIn, DateOnly date, TimeOnly start) =>
        checkIn.HasValue && !AttendanceRules.IsWeeklyOff(date) && AttendanceRules.IsLateArrival(checkIn.Value, date, start)
            ? " | Late by " + AttendanceRules.FormatHours(AttendanceRules.LateDuration(checkIn.Value, date, start)) : "";

    private static string EarlyExit(DateTime? checkOut, DateOnly date, TimeOnly start, TimeOnly end) =>
        checkOut.HasValue && checkOut.Value < date.ToDateTime(end).AddDays(end < start ? 1 : 0) ? " | Early exit" : "";

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
