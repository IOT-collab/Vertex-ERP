using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public sealed class EmployeeAttendanceReader(ApplicationDbContext db)
{
    // Raw WDMS punches can precede EmployeeId backfill. Only verified enrollment
    // or device links may associate them with a login; never match on a name.
    public IQueryable<AttendanceLog> OwnPunches(int employeeId) => db.AttendanceLogs.AsNoTracking().Where(log =>
        log.EmployeeId == employeeId || (log.EmployeeId == null
            && log.VerificationMode != AttendanceRules.FieldVerificationMode && log.VerificationMode != "Manual Approved"
            && (db.BiometricEnrollments.Any(link => link.EmployeeId == employeeId && link.Status == "Linked" && link.EnrollCode == log.DeviceUserId.Trim())
                || db.EmployeeDeviceMappings.Any(link => link.EmployeeId == employeeId && link.IsActive
                    && link.BiometricDeviceId == log.BiometricDeviceId && link.DeviceUserId == log.DeviceUserId.Trim()))));

    public async Task<IReadOnlyList<EmployeeAttendanceDay>> ReadAsync(Employee employee, DateOnly from, DateOnly to, CancellationToken token = default)
    {
        var first = AttendanceRules.PunchWindow(from, employee.ShiftStartTime, employee.ShiftEndTime).From;
        var last = AttendanceRules.PunchWindow(to, employee.ShiftStartTime, employee.ShiftEndTime).To;
        var punches = await OwnPunches(employee.Id).Where(log => log.PunchTime >= first && log.PunchTime < last)
            .Include(log => log.BiometricDevice).OrderBy(log => log.PunchTime).ToListAsync(token);
        var leaves = await db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.Status == "Approved" && x.FromDate <= to && x.ToDate >= from).ToListAsync(token);
        return BuildDays(employee, from, to, punches, leaves);
    }

    public static IReadOnlyList<EmployeeAttendanceDay> BuildDays(Employee employee, DateOnly from, DateOnly to,
        IEnumerable<AttendanceLog> logs, IEnumerable<LeaveRequest> approvedLeaves)
    {
        var all = logs.OrderBy(x => x.PunchTime).ToList();
        var leaves = approvedLeaves.Where(x => x.EmployeeId == employee.Id && x.Status == "Approved").ToList();
        var days = new List<EmployeeAttendanceDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var window = AttendanceRules.PunchWindow(date, employee.ShiftStartTime, employee.ShiftEndTime);
            var punches = all.Where(x => x.PunchTime >= window.From && x.PunchTime < window.To).ToList();
            var paired = AttendanceRules.PairPunches(punches.Select(x => (x.PunchTime, x.PunchState)));
            var hours = paired.CheckOut.HasValue ? paired.CheckOut.Value - paired.CheckIn!.Value : TimeSpan.Zero;
            var sources = punches.Select(x => x.VerificationMode == AttendanceRules.FieldVerificationMode || x.BiometricDevice?.CommunicationMode == AttendanceRules.FieldCommunicationMode
                ? "Field Attendance" : x.VerificationMode == "Manual Approved" ? "Manual Approved" : "Biometric / Thumb").Distinct();
            days.Add(new EmployeeAttendanceDay(date, paired.CheckIn, paired.CheckOut,
                AttendanceRules.StatusFor(paired.CheckIn, date, leaves.Any(x => x.FromDate <= date && x.ToDate >= date), employee.ShiftStartTime),
                punches.Count == 0 ? "--" : string.Join(" + ", sources))
            {
                PunchCount = punches.Count, WorkingHours = hours,
                Overtime = paired.CheckOut.HasValue ? AttendanceRules.CalculateOvertime(hours, employee.OvertimeEnabled, employee.ShiftStartTime, employee.ShiftEndTime) : TimeSpan.Zero,
                SiteDetails = string.Join("; ", punches.Where(x => !string.IsNullOrWhiteSpace(x.FieldSiteName)).Select(x => $"{x.PunchTime:HH:mm} · {x.FieldSiteName}").Distinct())
            });
        }
        return days.OrderByDescending(x => x.Date).ToList();
    }
}
