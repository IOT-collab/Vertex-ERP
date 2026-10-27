using VertexERP.Models;
using VertexERP.Services;

internal static class DashboardCategoryChecks
{
    public static void Run()
    {
        var categories = new Dictionary<int, string?> { [1] = EmployeeCategories.BlueCollar, [2] = EmployeeCategories.WhiteCollar, [3] = null };
        var rows = new[] {
            new DailyAttendanceViewModel { EmployeeId = 1, CheckIn = new(11, 1), IsLate = true, Status = "Late" },
            new DailyAttendanceViewModel { EmployeeId = 2, CheckIn = new(9, 45), Status = "Present" },
            new DailyAttendanceViewModel { EmployeeId = 0, CheckIn = new(9, 30), Status = "Present" },
            new DailyAttendanceViewModel { EmployeeId = 3, Status = "Absent" },
            new DailyAttendanceViewModel { EmployeeId = 2, Status = "Absent" },
            new DailyAttendanceViewModel { EmployeeId = 1, Status = "Leave" },
            new DailyAttendanceViewModel { EmployeeId = 2, Status = "Leave" },
            new DailyAttendanceViewModel { EmployeeId = 3, Status = "Holiday" }
        };
        var result = DashboardAttendanceCategories.Build(rows, categories);
        Check(result["Present"] == new DashboardCategoryCount(1, 1, 1), "Present includes late arrivals and unlinked WDMS records");
        Check(result["Late"] == new DashboardCategoryCount(1, 0, 0), "Late uses shift evaluation flag");
        Check(result["Absent"] == new DashboardCategoryCount(0, 1, 1), "Absent excludes leave and holidays");
        Check(result["Leave"] == new DashboardCategoryCount(1, 1, 0), "Leave splits approved leave records");
        categories[1] = EmployeeCategories.WhiteCollar;
        Check(DashboardAttendanceCategories.Build(rows, categories)["Late"] == new DashboardCategoryCount(0, 1, 0), "HR category changes update counts");
        Check(DashboardAttendanceCategories.Build([], categories).Values.All(x => x == new DashboardCategoryCount(0, 0, 0)), "Empty attendance produces zero in all categories");
        Console.WriteLine("6 dashboard category checks passed.");
    }
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
