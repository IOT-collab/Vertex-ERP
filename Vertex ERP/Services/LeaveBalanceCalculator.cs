using VertexERP.Models;

namespace VertexERP.Services;

public static class LeaveBalanceCalculator
{
    public static (decimal Earned, decimal Adjustment) Calculate(IEnumerable<LeaveMonth> months, string category)
    {
        var rows = months.ToList();
        var days = rows.Sum(x => x.WorkingDays);
        return category switch
        {
            "EL" => (decimal.Floor(days / 20), rows.Sum(x => x.AdjustedEL)),
            "CL" => (decimal.Floor(days / 40), rows.Sum(x => x.AdjustedCL)),
            "ML" => (decimal.Floor(days / 40), rows.Sum(x => x.AdjustedML)),
            "EW" => (rows.Sum(x => x.EarnedEW), rows.Sum(x => x.AdjustedEW)),
            _ => throw new ArgumentException("Unknown category")
        };
    }

    public static void ApplyRequests(IEnumerable<ManualLeaveBalance> balances, IEnumerable<LeaveRequest> requests)
    {
        var all = requests.ToList();
        foreach (var balance in balances)
        {
            var matching = all.Where(x => x.EmployeeId == balance.EmployeeId && Category(x.LeaveType) == balance.Category);
            balance.ApprovedDays = Days(matching, balance.Year, "Approved");
            balance.PendingDays = Days(matching, balance.Year, "Pending");
        }
    }
    public static void IncludeUnfilled(List<ManualLeaveBalance> balances, IEnumerable<LeaveRequest> requests, int year)
    {
        var all = requests.ToList();
        foreach (var group in all.Where(x => x.FromDate.Year <= year && x.ToDate.Year >= year && (x.Status == "Approved" || x.Status == "Pending"))
                     .GroupBy(x => (x.EmployeeId, Category: Category(x.LeaveType))))
            if (Categories.Contains(group.Key.Category) && !balances.Any(x => x.EmployeeId == group.Key.EmployeeId && x.Year == year && x.Category == group.Key.Category))
                balances.Add(new ManualLeaveBalance { EmployeeId = group.Key.EmployeeId, Year = year, Category = group.Key.Category });
        ApplyRequests(balances, all);
    }
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
        var days = new Dictionary<int, decimal>();
        foreach (var request in requests.Where(x => x.Status == status))
            for (var day = Math.Max(first, request.FromDate.DayNumber); day <= Math.Min(last, request.ToDate.DayNumber); day++)
                days[day] = Math.Max(days.GetValueOrDefault(day), request.IsHalfDay ? .5m : 1m);
        return days.Values.Sum();
    }

    public static decimal Used(decimal approved, decimal adjustment) => Math.Max(0, approved + adjustment);
}
