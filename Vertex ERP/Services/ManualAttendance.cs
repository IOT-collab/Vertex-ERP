using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class ManualAttendance
{
    public const string VerificationMode = "Manual Approved";
    public static (List<(DateTime Time, string State)> Punches, string? Error) Plan(DateOnly date, TimeOnly checkIn,
        TimeOnly? checkOut, IEnumerable<AttendanceLog> biometricPunches, TimeOnly? shiftStart = null, TimeOnly? shiftEnd = null)
    {
        var from = AttendanceRules.CheckInAt(date, checkIn, shiftStart ?? new(9,45), shiftEnd ?? new(18,30));
        var to = checkOut.HasValue ? AttendanceRules.CheckOutAt(date, checkIn, checkOut.Value, shiftStart ?? new(9,45), shiftEnd ?? new(18,30)) : from;
        if (checkOut.HasValue && to <= from) return ([], "Check-out must be later than check-in on the selected date.");
        if (shiftStart.HasValue && shiftEnd.HasValue)
        {
            var window = AttendanceRules.PunchWindow(date, shiftStart.Value, shiftEnd.Value);
            if (from < window.From || from >= window.To || to < window.From || to >= window.To)
                return ([], "Entered times fall outside the selected employee's shift date. Review the date and times.");
        }
        var existing = biometricPunches.ToList();
        if (existing.Any(x => x.PunchTime < from || x.PunchTime > to))
            return ([], "Existing biometric/field punches fall outside these times. Review the actual punches; they cannot be overwritten by manual entry.");
        var result = new List<(DateTime, string)>();
        if (!existing.Any(x => x.PunchTime == from)) result.Add((from, "Check In"));
        if (checkOut.HasValue && !existing.Any(x => x.PunchTime == to)) result.Add((to, "Check Out"));
        return (result, null);
    }

    // Caller runs this inside an execution strategy and a transaction, using this lock for all manual writes.
    public static Task LockAsync(ApplicationDbContext db) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(827431902)");

    public static async Task<string?> MarkAsync(ApplicationDbContext db, Employee employee, DateOnly date,
        TimeOnly checkIn, TimeOnly? checkOut, string reason, int actorId, int? requestId = null)
    {
        var window = AttendanceRules.PunchWindow(date, employee.ShiftStartTime, employee.ShiftEndTime);
        var logs = await new EmployeeAttendanceReader(db).OwnPunches(employee.Id)
            .Where(x => x.PunchTime >= window.From && x.PunchTime < window.To).ToListAsync();
        var plan = Plan(date, checkIn, checkOut, logs.Where(x => x.VerificationMode != VerificationMode), employee.ShiftStartTime, employee.ShiftEndTime);
        if (plan.Error != null) return plan.Error;
        if (!employee.IsActive) return "Select an active employee.";
        // Replace only prior manual entries. Keep all biometric and field evidence unchanged.
        var manualIds = logs.Where(x => x.VerificationMode == VerificationMode).Select(x => x.Id).ToList();
        if (manualIds.Count > 0)
        {
            db.AttendanceLogs.RemoveRange(await db.AttendanceLogs.Where(x => manualIds.Contains(x.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
        var device = await db.BiometricDevices.FirstOrDefaultAsync(x => x.SerialNumber == "MANUAL-ENTRY");
        if (device == null)
        {
            device = new BiometricDevice { Name = "ERP Manual Attendance", SerialNumber = "MANUAL-ENTRY", Model = "ERP", CommunicationMode = "Manual" };
            db.BiometricDevices.Add(device); await db.SaveChangesAsync();
        }
        var actor = await db.AppUsers.AsNoTracking().Where(x => x.Id == actorId).Select(x => x.FullName).FirstOrDefaultAsync();
        foreach (var punch in plan.Punches)
        {
            db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = employee.Id, DeviceUserId = employee.EmployeeCode,
                BiometricDeviceId = device.Id, PunchTime = punch.Time, PunchState = punch.State, VerificationMode = VerificationMode,
                UniqueHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"manual|{employee.Id}|{punch.Time:O}"))),
                RawPayload = $"Manual attendance | Marked by: {actor ?? "User"} (#{actorId}) | Request: {requestId} | Reason: {reason}" });
        }
        return null;
    }

    public const string CorrectionCode = "HR Correction";

    public static (DateTime? CheckIn, DateTime? CheckOut, bool NeedsReview) EffectivePunches(IEnumerable<AttendanceLog> logs)
    {
        var all = logs.ToList();
        var original = AttendanceRules.PairPunches(all.Select(x => (x.PunchTime, x.PunchState)));
        AttendanceLog? Latest(string state) => all.Where(x => x.VerificationMode == VerificationMode
                && x.WorkCode == CorrectionCode && x.PunchState == state)
            .OrderByDescending(x => x.ReceivedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
        var start = Latest("Check In")?.PunchTime ?? original.CheckIn;
        var end = Latest("Check Out")?.PunchTime ?? original.CheckOut;
        return (start, end > start ? end : null, false);
    }

    public static async Task<string?> CorrectAsync(ApplicationDbContext db, Employee employee, DateOnly date,
        string mode, TimeOnly? checkIn, TimeOnly? checkOut)
    {
        if (mode is not ("Both" or "CheckIn" or "CheckOut")) return "Select the time to modify.";
        if ((mode != "CheckOut" && !checkIn.HasValue) || (mode != "CheckIn" && !checkOut.HasValue))
            return "Enter the selected punch time(s).";
        var window = AttendanceRules.PunchWindow(date, employee.ShiftStartTime, employee.ShiftEndTime);
        var logs = await new EmployeeAttendanceReader(db).OwnPunches(employee.Id)
            .Where(x => x.PunchTime >= window.From && x.PunchTime < window.To).ToListAsync();
        var old = EffectivePunches(logs);
        var start = mode == "CheckOut" ? old.CheckIn : AttendanceRules.CheckInAt(date, checkIn!.Value, employee.ShiftStartTime, employee.ShiftEndTime);
        if (!start.HasValue) return "No existing check-in found. Select Both and enter check-in and check-out.";
        var end = mode == "CheckIn" ? old.CheckOut : AttendanceRules.CheckOutAt(date, TimeOnly.FromDateTime(start.Value), checkOut!.Value, employee.ShiftStartTime, employee.ShiftEndTime);
        if (end.HasValue && end <= start) return "Check-out must be later than check-in. Review the selected times.";
        if (start < window.From || start >= window.To || (end.HasValue && (end < window.From || end >= window.To)))
            return "Entered times fall outside this shift date.";
        var device = await db.BiometricDevices.FirstOrDefaultAsync(x => x.SerialNumber == "MANUAL-ENTRY");
        if (device == null)
        {
            device = new BiometricDevice { Name = "ERP Manual Attendance", SerialNumber = "MANUAL-ENTRY", Model = "ERP", CommunicationMode = "Manual" };
            db.BiometricDevices.Add(device); await db.SaveChangesAsync();
        }
        var recorded = DateTime.UtcNow;
        async Task Replace(DateTime time, string state, DateTime? previous)
        {
            var source = logs.Where(x => x.PunchTime == previous)
                .OrderByDescending(x => x.WorkCode == CorrectionCode && x.PunchState == state)
                .ThenByDescending(x => x.ReceivedAtUtc).FirstOrDefault();
            AttendanceLog entry;
            if (source != null)
            {
                entry = await db.AttendanceLogs.FindAsync(source.Id) ?? throw new InvalidOperationException("Punch no longer exists.");
                entry.RawPayload = string.Empty;
            }
            else
            {
                entry = new AttendanceLog { EmployeeId = employee.Id, DeviceUserId = employee.EmployeeCode,
                    BiometricDeviceId = device.Id, RawPayload = string.Empty };
                db.AttendanceLogs.Add(entry);
            }
            entry.EmployeeId = employee.Id;
            entry.PunchTime = time; entry.PunchState = state; entry.VerificationMode = VerificationMode;
            entry.WorkCode = CorrectionCode; entry.ReceivedAtUtc = recorded;
            if (source == null) entry.UniqueHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));
        }
        if (mode != "CheckOut") await Replace(start.Value, "Check In", old.CheckIn);
        if (mode != "CheckIn") await Replace(end!.Value, "Check Out", old.CheckOut);
        return null;
    }

    public static string Details(IEnumerable<AttendanceLog> punches) => string.Join("; ", punches
        .Where(x => x.VerificationMode == VerificationMode && x.WorkCode != CorrectionCode)
        .GroupBy(x => x.RawPayload).Select(g => $"{g.Key} | Recorded: {FieldAttendanceClock.IndiaTime(new DateTimeOffset(DateTime.SpecifyKind(g.Max(x => x.ReceivedAtUtc), DateTimeKind.Utc))):dd MMM yyyy HH:mm} IST"));
}
