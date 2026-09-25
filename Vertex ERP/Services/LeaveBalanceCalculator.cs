using VertexERP.Models;

namespace VertexERP.Services;

public static class LeaveBalanceCalculator
{
    public static readonly string[] Categories = ["EL", "CL", "EW", "ML"];
    public static string Category(string? type) => type?.Trim().ToUpperInvariant() switch
    {
        "EL" or "EARNED LEAVE" => "EL",
        "CL" or "CASUAL LEAVE" => "CL",
        "EW" => "EW",
        "ML" => "ML",
        _ => "Other"
    };
    // Count calendar dates once, including requests that cross a year boundary.
    public static decimal Days(IEnumerable<LeaveRequest> requests, int year, string status)
    {
        var first = new DateOnly(year, 1, 1).DayNumber;
        var last = new DateOnly(year, 12, 31).DayNumber;
        var days = new HashSet<int>();
        foreach (var request in requests.Where(x => x.Status == status))
            for (var day = Math.Max(first, request.FromDate.DayNumber); day <= Math.Min(last, request.ToDate.DayNumber); day++)
                days.Add(day);
        return days.Count;
    }

    public static decimal Used(decimal approved, decimal adjustment) => Math.Max(0, approved + adjustment);
}
