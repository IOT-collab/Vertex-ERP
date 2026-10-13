namespace VertexERP.Services;

public static class AttendanceRules
{
    public const string FieldCommunicationMode = "Field";
    public const string FieldVerificationMode = "GPS Field";

    // The complete 09:30 minute is on time; late starts at 09:31:00.
    public static bool IsLateArrival(DateTime checkIn) =>
        TimeOnly.FromDateTime(checkIn) >= new TimeOnly(9, 31);

    public static bool IsWeeklyOff(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

    private static readonly IReadOnlyList<VertexERP.Models.CompanyHoliday> CompanyHolidays =
        new VertexERP.Models.CompanyHolidayCalendar(DateTimeOffset.UtcNow).Holidays;

    public static string? HolidayName(DateOnly date) =>
        CompanyHolidays.FirstOrDefault(holiday => holiday.Start <= date && holiday.End >= date)?.Name
        ?? (IsWeeklyOff(date) ? "Sunday" : null);

    public static bool IsHoliday(DateOnly date) => HolidayName(date) != null;

    public static string NoPunchStatus(DateOnly date, bool approvedLeave = false) =>
        IsHoliday(date) ? "Holiday" : approvedLeave ? "Leave" : "Absent";

    public static string StatusFor(DateTime? checkIn, DateOnly date, bool approvedLeave = false) =>
        checkIn.HasValue ? (IsLateArrival(checkIn.Value) ? "Late" : "Present") : NoPunchStatus(date, approvedLeave);

    public static (string Status, string Remark, bool IsLate, TimeSpan Hours) EvaluateDay(
        DateTime? checkIn, DateTime? checkOut, DateOnly date, DateOnly today, TimeOnly startTime)
    {
        if (!checkIn.HasValue) return (NoPunchStatus(date), HolidayName(date) ?? "No punches recorded", false, TimeSpan.Zero);
        var late = IsLateArrival(checkIn.Value);
        var hours = checkOut.HasValue && checkOut.Value > checkIn.Value
            ? checkOut.Value - checkIn.Value : TimeSpan.Zero;
        return (late ? "Late" : "Present", late ? "Late arrival" : "On time", late, hours);
    }

    public static string FormatHours(TimeSpan hours) => $"{(int)hours.TotalHours}h {hours.Minutes:D2}m";

    public static TimeSpan CalculateOvertime(TimeSpan workingHours) =>
        workingHours > TimeSpan.FromMinutes(510) ? workingHours - TimeSpan.FromMinutes(510) : TimeSpan.Zero;

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
