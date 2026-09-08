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

        var normalized = ordered.Select(punch => (punch.Time, Action: NormalizePunchAction(punch.State))).ToList();
        if (normalized.All(punch => punch.Action is null))
            return (ordered[0].Time, ordered.Count > 1 ? ordered[^1].Time : null, false);

        var checkIn = normalized.FirstOrDefault(punch => punch.Action == "Check In");
        if (checkIn == default)
            return (null, null, true);

        var checkOut = normalized.LastOrDefault(punch => punch.Action == "Check Out" && punch.Time > checkIn.Time);
        return (checkIn.Time, checkOut == default ? null : checkOut.Time, false);
    }
}
