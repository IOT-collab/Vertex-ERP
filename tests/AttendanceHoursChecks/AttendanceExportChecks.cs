using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VertexERP.Controllers;
using VertexERP.Models;
using VertexERP.Repositories;
using VertexERP.Services;

static class AttendanceExportChecks
{
    public static async Task Run()
    {
        void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
        }
        foreach (var (minutes, expected) in new[] { (0, 0), (509, 0), (510, 0), (511, 1), (600, 90) })
            Check(AttendanceRules.CalculateOvertime(TimeSpan.FromMinutes(minutes)).TotalMinutes == expected, $"Overtime boundary {minutes} minutes");

        var day = new DateOnly(2026, 9, 1);
        var employee = new Employee { Id = 1, EmployeeCode = "E001", FullName = "Test Employee", Department = "Service" };
        AttendanceLog Punch(int hour, int minute, string action, string site) => new()
        {
            EmployeeId = 1, Employee = employee, PunchTime = day.ToDateTime(new TimeOnly(hour, minute)),
            PunchState = action, VerificationMode = AttendanceRules.FieldVerificationMode,
            FieldSiteName = site, Latitude = 28.6139m, Longitude = 77.2090m
        };
        var repository = DispatchProxy.Create<IBiometricRepository, ExportRepository>();
        var stub = (ExportRepository)(object)repository;
        stub.Employees = new[] { employee };
        stub.Logs = new[] { Punch(9, 0, "Check In", "Site A & Works"), Punch(19, 0, "Check Out", "Site B") };
        var service = new AttendanceProcessingService(repository, Options.Create(new AttendanceOptions()), NullLogger<AttendanceProcessingService>.Instance);
        var record = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(record.SiteCheckIns.Contains("Site A & Works") && record.SiteCheckIns.Contains("09:00 AM") && record.SiteCheckIns.Contains("28.6139,77.2090"), "Site check-in retains time, site and GPS");
        Check(record.SiteCheckOuts.Contains("Site B") && record.SiteCheckOuts.Contains("07:00 PM"), "Site checkout retains its own location and time");
        Check(record.OvertimeDisplay == "1h 30m", "Completed site day overtime");
        var controller = new MainController(null!, service, null!, null!, null!, null!);
        var csv = (FileContentResult)await controller.ExportAttendance(null, null, day, null);
        var csvText = Encoding.UTF8.GetString(csv.FileContents);
        Check(csvText.Contains("Overtime,Site Check In") && csvText.Contains("1h 30m") && csvText.Contains("Site A & Works") && csvText.Contains("Site B"), "Daily export includes overtime and both site locations");
        var monthly = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        var xml = XDocument.Parse(Encoding.UTF8.GetString(monthly.FileContents));
        XNamespace ns = "urn:schemas-microsoft-com:office:spreadsheet";
        var sheets = xml.Root!.Elements(ns + "Worksheet").ToList();
        Check(sheets.Count == 2 && sheets[1].Attribute(ns + "Name")!.Value == "Daily Details", "Monthly workbook includes daily details worksheet");
        var pivotRows = sheets[0].Descendants(ns + "Row").ToList();
        Check(pivotRows[1].Elements(ns + "Cell").Count() == 38 && pivotRows[2].Elements(ns + "Cell").Count() == 38, "Monthly headers and data columns align");
        Check(pivotRows[2].Elements(ns + "Cell").ElementAt(7).Value == "1h 30m", "Monthly overtime sums daily excess without subtracting absent days");
        Check(sheets[1].Value.Contains("Site A & Works") && sheets[1].Value.Contains("Site B"), "Monthly details preserve site text and XML escaping");

        stub.Logs = new[] { Punch(9, 0, "Check In", "Site A") };
        var open = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(open.OvertimeDisplay == "—" && open.SiteCheckOuts == "", "Open attendance does not invent checkout or overtime");

        var inactive = new Employee { Id = 2, EmployeeCode = "INACTIVE002", FullName = "Inactive Employee", Department = "Former Department", IsActive = false };
        var absent = new Employee { Id = 3, EmployeeCode = "E003", FullName = "Active Absent", Department = "Service" };
        stub.Employees = new[] { employee, absent };
        stub.Logs = new[] {
            Punch(9, 0, "Check In", "Site A"),
            new AttendanceLog { EmployeeId = inactive.Id, Employee = inactive, PunchTime = day.ToDateTime(new TimeOnly(11, 0)), PunchState = "IN" }
        };
        var roster = await service.GetDailyAttendanceAsync(day, null, null, null);
        Check(roster.Records.Count == 3 && roster.Records.Any(r => r.EmployeeId == inactive.Id), "Inactive employee with punches remains visible in attendance");
        Check(roster.PresentCount == 2 && roster.AbsentCount == 1 && roster.LateCount == 1, "Attendance totals include recorded inactive employee punches");
        Check(!roster.Departments.Contains("Former Department"), "Inactive department is excluded from attendance filters");
        Check((await service.GetDailyAttendanceAsync(day, "INACTIVE002", null, null)).Records.Count == 1, "Inactive employee punches remain searchable");
        var activeCsv = (FileContentResult)await controller.ExportAttendance(null, null, day, null);
        Check(Encoding.UTF8.GetString(activeCsv.FileContents).Contains("INACTIVE002"), "Daily export includes inactive employee punches");
        foreach (var exportMonth in new[] { "2026-09", DateTime.Today.ToString("yyyy-MM"), DateTime.Today.AddMonths(-1).ToString("yyyy-MM"), "2024-02" }.Distinct())
        {
            var monthFile = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: exportMonth);
            var monthXml = XDocument.Parse(Encoding.UTF8.GetString(monthFile.FileContents));
            Check(monthFile.FileDownloadName == $"Attendance-Pivot-{exportMonth}.xls" && monthXml.Root!.Elements(ns + "Worksheet").Count() == 2,
                $"Monthly export downloads valid workbook for {exportMonth}");
            if (exportMonth == "2026-09")
            {
                var inactiveRow = monthXml.Root!.Elements(ns + "Worksheet").First().Descendants(ns + "Row")
                    .Single(row => row.Elements(ns + "Cell").FirstOrDefault()?.Value == "INACTIVE002");
                Check(inactiveRow.Elements(ns + "Cell").Count() == 38 && inactiveRow.Value.Contains("—"),
                    "Inactive employee with partial-month punches exports missing days without failure");
            }
        }
        stub.Employees = new[] { employee, absent, inactive };
        inactive.IsActive = true;
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Any(r => r.EmployeeId == inactive.Id), "Reactivated employee attendance is visible again");
    }
}

public class ExportRepository : DispatchProxy
{
    public IReadOnlyList<Employee> Employees = Array.Empty<Employee>();
    public IReadOnlyList<AttendanceLog> Logs = Array.Empty<AttendanceLog>();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "GetActiveEmployeesAsync" => Task.FromResult(Employees),
        "GetAttendanceLogsAsync" => Task.FromResult<IReadOnlyList<AttendanceLog>>(Logs.Where(log => log.PunchTime >= (DateTime)args![0]! && log.PunchTime < (DateTime)args[1]!).ToList()),
        _ => throw new NotSupportedException(method.Name)
    };
}
