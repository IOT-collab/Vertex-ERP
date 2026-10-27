using VertexERP.Models;

namespace VertexERP.Services;

public static class DashboardAttendanceCategories
{
    public static IReadOnlyDictionary<string, DashboardCategoryCount> Build(
        IEnumerable<DailyAttendanceViewModel> records, IReadOnlyDictionary<int, string?> categories)
    {
        var rows = records.ToList();
        return new Dictionary<string, DashboardCategoryCount>
        {
            ["Present"] = Count(rows.Where(row => row.CheckIn.HasValue)),
            ["Absent"] = Count(rows.Where(row => row.Status == "Absent")),
            ["Late"] = Count(rows.Where(row => row.IsLate)),
            ["Leave"] = Count(rows.Where(row => row.Status == "Leave"))
        };

        DashboardCategoryCount Count(IEnumerable<DailyAttendanceViewModel> matching)
        {
            var blue = 0; var white = 0; var unassigned = 0;
            foreach (var row in matching)
            {
                var category = categories.GetValueOrDefault(row.EmployeeId);
                if (category == EmployeeCategories.BlueCollar) blue++;
                else if (category == EmployeeCategories.WhiteCollar) white++;
                else unassigned++;
            }
            return new(blue, white, unassigned);
        }
    }
}
