namespace VertexERP.Services;

public static class AttendanceRules
{
    public const string FieldCommunicationMode = "Field";
    public const string FieldVerificationMode = "GPS Field";

    public static bool IsWeeklyOff(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday;

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
