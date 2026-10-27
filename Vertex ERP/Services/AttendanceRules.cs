namespace VertexERP.Services;

public static class AttendanceRules
{
    public const string FieldCommunicationMode = "Field";
    public const string FieldVerificationMode = "GPS Field";

    // Office check-in at 09:45 is on time; later arrivals are late.
    public static bool IsLateArrival(DateTime checkIn) =>
        IsLateArrival(checkIn, DateOnly.FromDateTime(checkIn), new TimeOnly(9, 45));

    public static bool IsLateArrival(DateTime checkIn, DateOnly shiftDate, TimeOnly startTime) =>
        LateDuration(checkIn, shiftDate, startTime) > TimeSpan.Zero;

    // The 09:30 office shift allows arrival through 09:45. Other assigned
    // shifts retain their own start time as the cutoff.
    public static TimeOnly ArrivalCutoff(TimeOnly startTime) =>
        startTime == new TimeOnly(9, 30) ? new TimeOnly(9, 45) : startTime;

    public static TimeSpan LateDuration(DateTime checkIn, DateOnly shiftDate, TimeOnly startTime)
    {
        var cutoff = shiftDate.ToDateTime(ArrivalCutoff(startTime));
        return checkIn > cutoff ? checkIn - cutoff : TimeSpan.Zero;
    }

    public static DateTime CheckInAt(DateOnly date, TimeOnly checkIn, TimeOnly shiftStart, TimeOnly shiftEnd) =>
        date.ToDateTime(checkIn).AddDays(shiftEnd < shiftStart && date.ToDateTime(checkIn) < PunchWindow(date, shiftStart, shiftEnd).From ? 1 : 0);

    public static DateTime CheckOutAt(DateOnly date, TimeOnly checkIn, TimeOnly checkOut, TimeOnly shiftStart, TimeOnly shiftEnd) =>
        date.ToDateTime(checkOut).AddDays(shiftEnd < shiftStart
            && (checkOut < checkIn || CheckInAt(date, checkIn, shiftStart, shiftEnd).Date > date.ToDateTime(TimeOnly.MinValue)) ? 1 : 0);

    public static TimeSpan ShiftDuration(TimeOnly start, TimeOnly end) =>
        end > start ? end - start : TimeSpan.FromDays(1) - (start - end);

    // Overnight punches belong to the date the shift starts. A boundary halfway
    // through the off-duty gap keeps early arrivals and next-morning overtime together.
    public static (DateTime From, DateTime To) PunchWindow(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var boundary = end < start ? end.ToTimeSpan() + (start - end) / 2 : TimeSpan.Zero;
        var from = date.ToDateTime(TimeOnly.MinValue).Add(boundary);
        return (from, from.AddDays(1));
    }

    public static DateOnly ShiftDate(DateTime time, TimeOnly start, TimeOnly end)
    {
        var date = DateOnly.FromDateTime(time);
        return time < PunchWindow(date, start, end).From ? date.AddDays(-1) : date;
    }

    public static bool IsWeeklyOff(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

    private static readonly IReadOnlyList<VertexERP.Models.CompanyHoliday> CompanyHolidays =
        new VertexERP.Models.CompanyHolidayCalendar(DateTimeOffset.UtcNow).Holidays;

    public static string? HolidayName(DateOnly date) =>
        CompanyHolidays.FirstOrDefault(holiday => holiday.Start <= date && holiday.End >= date)?.Name
        ?? (IsWeeklyOff(date) ? "Sunday" : null);

    public static bool IsHoliday(DateOnly date) => HolidayName(date) != null;

    public static string NoPunchStatus(DateOnly date, bool approvedLeave = false) =>
        IsHoliday(date) ? "Holiday" : approvedLeave ? "Leave" : "Absent";

    public static string StatusFor(DateTime? checkIn, DateOnly date, bool approvedLeave = false, TimeOnly? startTime = null) =>
        checkIn.HasValue ? (!IsWeeklyOff(date) && IsLateArrival(checkIn.Value, date, startTime ?? new TimeOnly(9, 45)) ? "Late" : "Present") : NoPunchStatus(date, approvedLeave);

    public static (string Status, string Remark, bool IsLate, TimeSpan Hours) EvaluateDay(
        DateTime? checkIn, DateTime? checkOut, DateOnly date, DateOnly today, TimeOnly startTime)
    {
        if (!checkIn.HasValue) return (NoPunchStatus(date), HolidayName(date) ?? "No punches recorded", false, TimeSpan.Zero);
        var late = !IsWeeklyOff(date) && IsLateArrival(checkIn.Value, date, startTime);
        var hours = checkOut.HasValue && checkOut.Value > checkIn.Value
            ? checkOut.Value - checkIn.Value : TimeSpan.Zero;
        return (late ? "Late" : "Present", IsWeeklyOff(date) ? "Worked on Sunday" : late ? "Late arrival" : "On time", late, hours);
    }

    public static string FormatHours(TimeSpan hours) => $"{(int)hours.TotalHours}h {hours.Minutes:D2}m";

    public static TimeSpan CalculateOvertime(TimeSpan workingHours) =>
        CalculateOvertime(workingHours, true, new TimeOnly(9, 45), new TimeOnly(18, 30));

    public static TimeSpan CalculateOvertime(TimeSpan workingHours, bool enabled, TimeOnly start, TimeOnly end) =>
        enabled && workingHours > ShiftDuration(start, end) ? workingHours - ShiftDuration(start, end) : TimeSpan.Zero;

    public static string? NormalizePunchAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant() switch
        {
            "IN" or "CHECK IN" or "CHECK-IN" or "CHECKIN" or "0" or "3" or "4" => "Check In",
            "OUT" or "CHECK OUT" or "CHECK-OUT" or "CHECKOUT" or "1" or "2" or "5" => "Check Out",
            _ => null
        };
    }

    public static (DateTime? CheckIn, DateTime? CheckOut, bool NeedsReview) PairPunches(IEnumerable<(DateTime Time, string? State)> punches)
    {
        var ordered = punches.OrderBy(punch => punch.Time).ToList();
        if (ordered.Count == 0) return (null, null, false);

        // Attendance uses chronological punches because several biometric
        // terminals send unreliable IN/OUT state values. The first punch is
        // check-in and the last distinct punch is check-out.
        var checkIn = ordered[0].Time;
        var checkOut = ordered.Count > 1 && ordered[^1].Time > checkIn
            ? ordered[^1].Time
            : (DateTime?)null;
        return (checkIn, checkOut, false);
    }
}
