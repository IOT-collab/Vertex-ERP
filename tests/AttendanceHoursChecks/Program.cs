using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
var sunday = new DateOnly(2026, 10, 11);
Check("Sunday morning punch", sunday.ToDateTime(new TimeOnly(9, 0)), null, sunday,
    "Present", false, "Worked on Sunday", 0);
Check("Sunday afternoon single punch", sunday.ToDateTime(new TimeOnly(14, 0)), null, sunday,
    "Present", false, "Worked on Sunday", 0);
Check("Sunday worked hours retained", sunday.ToDateTime(new TimeOnly(10, 0)), sunday.ToDateTime(new TimeOnly(18, 0)), sunday,
    "Present", false, "Worked on Sunday", 480);
if (AttendanceRules.StatusFor(sunday.ToDateTime(new TimeOnly(14, 0)), sunday, true) != "Present"
    || AttendanceRules.StatusFor(null, sunday, true) != "Holiday")
    throw new Exception("Sunday status must prioritize punches, then holiday, over approved leave.");
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
Check("09:31:00 remains present", At(9, 31), null, today, "Present", false, "On time", 0);
Check("09:44:59 remains present", At(9, 44).AddSeconds(59), null, today, "Present", false, "On time", 0);
Check("last tick before 09:45 remains present", At(9, 45).AddTicks(-1), null, today, "Present", false, "On time", 0);
Check("09:45:00 is late", At(9, 45), null, today, "Late", true, "Late arrival", 0);
Check("09:45:59 is late", At(9, 45).AddSeconds(59), null, today, "Late", true, "Late arrival", 0);
Check("09:46:00 is late", At(9, 46), null, today, "Late", true, "Late arrival", 0);
if (AttendanceRules.IsLateArrival(At(9, 44).AddSeconds(59))
    || !AttendanceRules.IsLateArrival(At(9, 45))
    || AttendanceRules.StatusFor(At(9, 44).AddSeconds(59), today) != "Present"
    || AttendanceRules.StatusFor(At(9, 45), today) != "Late")
    throw new Exception("Default attendance helpers must use the same 09:45:00 cutoff.");
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
var sundayPunch = sunday.ToDateTime(new TimeOnly(14, 0));
var sundayMonth = new VertexERP.Models.EmployeeAttendanceViewModel
{
    Days = new[]
    {
        new VertexERP.Models.EmployeeAttendanceDay(sunday, sundayPunch, null, AttendanceRules.StatusFor(sundayPunch, sunday), "Biometric"),
        new VertexERP.Models.EmployeeAttendanceDay(sunday.AddDays(7), null, null, AttendanceRules.StatusFor(null, sunday.AddDays(7)), "--")
    }
};
if (sundayMonth.Present != 1 || sundayMonth.Holidays != 1 || sundayMonth.Absent != 0 || sundayMonth.Late != 0)
    throw new Exception("Sunday punches must increase present count; unworked Sundays must only increase holidays.");
Console.WriteLine("PASS: Sunday present and holiday counts");

var customStart = new TimeOnly(10, 0);
if (AttendanceRules.IsLateArrival(At(10, 0).AddSeconds(59), today, customStart)
    || !AttendanceRules.IsLateArrival(At(10, 1), today, customStart))
    throw new Exception("Assigned shift must preserve the complete start minute grace.");
Console.WriteLine("PASS: Assigned start-minute cutoff");
foreach (var form in new object[] { new VertexERP.Models.EmployeeFormViewModel { ShiftStartTime = customStart, ShiftEndTime = customStart }, new VertexERP.Models.HrAddEmployeeViewModel { ShiftStartTime = customStart, ShiftEndTime = customStart } })
{
    var errors = ((System.ComponentModel.DataAnnotations.IValidatableObject)form).Validate(new System.ComponentModel.DataAnnotations.ValidationContext(form));
    if (!errors.Any(e => e.MemberNames.Contains("ShiftEndTime"))) throw new Exception("Equal shift times must be rejected.");
}
Console.WriteLine("PASS: Both employee forms reject zero-length shifts");

using (var schemaDb = new VertexERP.Data.ApplicationDbContext(new DbContextOptionsBuilder<VertexERP.Data.ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options))
{
    var script = schemaDb.GetService<IMigrator>().GenerateScript("20261012064048_AddLeaveManagerReview", "20261015120000_AddEmployeeShiftAndOvertime");
    if (!script.Contains("OvertimeEnabled") || !script.Contains("DEFAULT TRUE") || !script.Contains("09:30:00") || !script.Contains("18:00:00"))
        throw new Exception("Shift migration must preserve defaults for existing employees.");
    Console.WriteLine("PASS: Shift migration generates SQL with existing employee defaults without connecting to a database");
    var optInScript = schemaDb.GetService<IMigrator>().GenerateScript("20261015120000_AddEmployeeShiftAndOvertime", "20261015130000_RequireOvertimeOptIn");
    if (!optInScript.Contains("SET DEFAULT FALSE") || !optInScript.Contains("SET \"OvertimeEnabled\" = FALSE"))
        throw new Exception("Overtime opt-in migration must disable automatic overtime for existing and new employees.");
    Console.WriteLine("PASS: Opt-in migration starts existing employees inactive and disables automatic OT defaults");
}
