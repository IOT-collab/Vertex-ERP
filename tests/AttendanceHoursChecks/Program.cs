using VertexERP.Services;

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
Check("one second after cutoff", At(9, 30).AddSeconds(1), null, today, "Late", true, "Late arrival", 0);
Check("short attendance still present", At(9, 0), At(9, 1), today, "Present", false, "On time", 1);
