using VertexERP.Services;

await AttendanceExportChecks.Run();

var today = new DateOnly(2026, 9, 16);
var start = new TimeOnly(9, 30);
DateTime At(int hour, int minute) => today.ToDateTime(new TimeOnly(hour, minute));
void Check(string name, DateTime? checkIn, DateTime? checkOut, DateOnly date,
    string status, bool late, string remark, double minutes)
{
    var actual = AttendanceRules.EvaluateDay(checkIn, checkOut, date, today, start);
    if (actual.Status != status || actual.IsLate != late || actual.Remark != remark || actual.Hours.TotalMinutes != minutes)
        throw new Exception($"FAIL {name}: {actual}");
    Console.WriteLine($"PASS: {name}");
}
Check("late arrival completing exactly 8h 30m", At(10, 0), At(18, 30), today,
    "Late", true, "Late arrival", 510);
Check("one hour short", At(10, 0), At(17, 30), today,
    "Late", true, "Late arrival", 450);
Check("exact arrival cutoff", At(9, 30), At(18, 0), today,
    "Present", false, "On time", 510);
Check("one minute short", At(9, 30), At(17, 59), today,
    "Present", false, "On time", 509);
Check("current day open punch", At(9, 10), null, today,
    "Present", false, "On time", 0);
Check("past day missing punch", At(10, 0).AddDays(-1), null, today.AddDays(-1),
    "Late", true, "Late arrival", 0);
Check("no punches", null, null, today, "Absent", false, "No punches recorded", 0);
Check("Sunday without punches", null, null, new DateOnly(2026, 10, 11), "Holiday", false, "Sunday", 0);
Check("company holiday without punches", null, null, new DateOnly(2026, 10, 2), "Holiday", false, "Gandhi Jayanti", 0);
foreach (var date in new[] { 9, 10, 11 })
    Check("multi-day Diwali holiday " + date, null, null, new DateOnly(2026, 11, date), "Holiday", false, "Diwali, Gowardhan Puja & Bhai Dooj", 0);
Check("after Diwali", null, null, new DateOnly(2026, 11, 12), "Absent", false, "No punches recorded", 0);
Check("holiday list does not repeat into another year", null, null, new DateOnly(2027, 10, 2), "Absent", false, "No punches recorded", 0);
Check("actual work on holiday retained", new DateTime(2026, 10, 2, 9, 0, 0), new DateTime(2026, 10, 2, 18, 0, 0), new DateOnly(2026, 10, 2), "Present", false, "On time", 540);
Check("invalid punch order", At(10, 0), At(9, 0), today,
    "Late", true, "Late arrival", 0);
Check("extra hours", At(9, 0), At(19, 0), today,
    "Present", false, "On time", 600);
var paired = AttendanceRules.PairPunches(new[] {
    (At(10, 0), (string?)"IN"), (At(13, 0), (string?)"OUT"),
    (At(14, 0), (string?)"IN"), (At(18, 30), (string?)"OUT") });
Check("lunch included in total elapsed time", paired.CheckIn, paired.CheckOut, today,
    "Late", true, "Late arrival", 510);

Check("exact cutoff without checkout", At(9, 30), null, today, "Present", false, "On time", 0);
Check("09:30:01 remains present", At(9, 30).AddSeconds(1), null, today, "Present", false, "On time", 0);
Check("last tick before 09:31 remains present", At(9, 31).AddTicks(-1), null, today, "Present", false, "On time", 0);
Check("09:31:00 is late", At(9, 31), null, today, "Late", true, "Late arrival", 0);
Check("09:31:01 is late", At(9, 31).AddSeconds(1), null, today, "Late", true, "Late arrival", 0);
var onTimePunches = AttendanceRules.PairPunches(new[] { (At(9, 30).AddSeconds(59), (string?)"IN"), (At(18, 0), (string?)"OUT") });
Check("evening checkout does not make an on-time employee late", onTimePunches.CheckIn, onTimePunches.CheckOut, today, "Present", false, "On time", (At(18, 0) - At(9, 30).AddSeconds(59)).TotalMinutes);
Check("short attendance still present", At(9, 0), At(9, 1), today, "Present", false, "On time", 1);

var holidayCalendar = new VertexERP.Models.CompanyHolidayCalendar(DateTimeOffset.UtcNow);
foreach (var holiday in holidayCalendar.Holidays)
    for (var date = holiday.Start; date <= holiday.End; date = date.AddDays(1))
        Check("Company calendar coverage: " + holiday.Name + " " + date, null, null, date, "Holiday", false, holiday.Name, 0);
var octoberDays = Enumerable.Range(1, 13).Select(day => {
    var date = new DateOnly(2026, 10, day);
    return new VertexERP.Models.EmployeeAttendanceDay(date, null, null, AttendanceRules.NoPunchStatus(date), "--");
}).ToList();
var employeeMonth = new VertexERP.Models.EmployeeAttendanceViewModel { Days = octoberDays };
if (employeeMonth.Holidays != 3 || employeeMonth.Absent != 10) throw new Exception("Employee October summary must exclude Gandhi Jayanti and Sundays from absences.");
Console.WriteLine("PASS: Employee month summary separates holidays from absent days");
