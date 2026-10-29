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
        foreach (var (minutes, expected) in new[] { (0, 0), (524, 0), (525, 0), (526, 1), (600, 75) })
            Check(AttendanceRules.CalculateOvertime(TimeSpan.FromMinutes(minutes)).TotalMinutes == expected, $"Overtime boundary {minutes} minutes");

        var day = new DateOnly(2026, 9, 1);
        var employee = new Employee { Id = 1, IsBiometricProfilePending = true, OvertimeEnabled = true, EmployeeCode = "E001", FullName = "Test Employee", Department = "Service" };
        AttendanceLog Punch(int hour, int minute, string action, string site) => new()
        {
            EmployeeId = 1, Employee = employee, PunchTime = day.ToDateTime(new TimeOnly(hour, minute)),
            PunchState = action, VerificationMode = AttendanceRules.FieldVerificationMode,
            FieldSiteName = site, Latitude = 28.6139m, Longitude = 77.2090m
        };
        var repository = DispatchProxy.Create<IBiometricRepository, ExportRepository>();
        var stub = (ExportRepository)(object)repository;
        stub.Employees = new[] { employee };
        stub.Enrollments = new[]
        {
            new BiometricEnrollment { EnrollCode = "00108", EmployeeId = 1, Status = "Linked", FullName = "Sheet Name", WdmsName = "WDMS exact Name", Employee = employee, LastSeenUtc = DateTime.UtcNow },
            new BiometricEnrollment { EnrollCode = "109", EmployeeId = 2, Status = "Linked", FullName = "Inactive Employee", WdmsName = "Inactive WDMS Name" },
            new BiometricEnrollment { EnrollCode = "110", EmployeeId = 3, Status = "Linked", FullName = "Active Absent", WdmsName = "Absent WDMS Name" }
        };
        stub.Logs = new[] { Punch(9, 0, "Check In", "Site A & Works"), Punch(19, 0, "Check Out", "Site B") };
        var service = new AttendanceProcessingService(repository, Options.Create(new AttendanceOptions()), NullLogger<AttendanceProcessingService>.Instance);
        var record = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(record.EmpId == "00108" && record.EmployeeName == "WDMS exact Name", "Screen uses exact WDMS code with source name");
        stub.Enrollments[0].IsSheetReference = true;
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single().EmployeeName == "WDMS exact Name", "Imported sheet name cannot override WDMS name");
        stub.Enrollments[0].IsSheetReference = false;
        employee.FullName = "Updated Employee";
        employee.IsBiometricProfilePending = false;
        employee.Department = "Updated Department";
        var updatedProfile = (await service.GetDailyAttendanceAsync(day, "Updated Employee", "Updated Department", null)).Records.Single();
        Check(updatedProfile.EmployeeName == "Updated Employee" && updatedProfile.Department == "Updated Department", "HR profile changes update attendance name, department, search and filters");
        Check(EmployeeProfileDisplay.Name(employee, "WDMS exact Name") == "Updated Employee", "HR name takes precedence over old WDMS name");
        var profileController = new MainController(null!, service, null!, null!, null!, null!);
        var profileCsv = Encoding.UTF8.GetString(((FileContentResult)await profileController.ExportAttendance(null, null, day, null)).FileContents);
        Check(profileCsv.Contains("Updated Employee") && profileCsv.Contains("Updated Department") && !profileCsv.Contains("WDMS exact Name"), "Daily export reads latest HR profile");
        var profileMonthly = (FileContentResult)await profileController.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        var profileXml = XDocument.Parse(Encoding.UTF8.GetString(profileMonthly.FileContents));
        Check(profileXml.Root!.Value.Contains("Updated Employee") && profileXml.Root.Value.Contains("Updated Department"), "Monthly export reads latest HR profile");
        employee.IsBiometricProfilePending = true;
        employee.Department = "Service";
        employee.FullName = "Test Employee";
        Check((await service.GetDailyAttendanceAsync(day, "00108", null, null)).Records.Count == 1, "Search accepts WDMS enroll code");
        Check(record.SiteCheckIns.Contains("Site A & Works") && record.SiteCheckIns.Contains("09:00 AM") && record.SiteCheckIns.Contains("28.6139,77.2090"), "Site check-in retains time, site and GPS");
        Check(record.SiteCheckOuts.Contains("Site B") && record.SiteCheckOuts.Contains("07:00 PM"), "Site checkout retains its own location and time");
        Check(record.OvertimeDisplay == "1h 15m", "Completed site day overtime");
        var controller = new MainController(null!, service, null!, null!, null!, null!);
        var csv = (FileContentResult)await controller.ExportAttendance(null, null, day, null);
        var csvText = Encoding.UTF8.GetString(csv.FileContents);
        Check(csvText.Contains("\"00108\",\"WDMS exact Name\"") && !csvText.Contains("E001"), "Daily export uses WDMS identity instead of internal ERP code");
        Check(csvText.Contains("Overtime,Site Check In") && csvText.Contains("1h 15m") && csvText.Contains("Site A & Works") && csvText.Contains("Site B"), "Daily export includes overtime and both site locations");
        var originalLogs = stub.Logs;
        var updatedIn = Punch(9, 40, "Check In", "");
        updatedIn.VerificationMode = ManualAttendance.VerificationMode; updatedIn.WorkCode = ManualAttendance.CorrectionCode;
        updatedIn.RawPayload = "HR correction | Previous: 09:55 | Updated: 09:40 | Reason: Corrected punch";
        var updatedOut = Punch(18, 0, "Check Out", "");
        updatedOut.VerificationMode = ManualAttendance.VerificationMode; updatedOut.WorkCode = ManualAttendance.CorrectionCode;
        stub.Logs = new[] { updatedIn, updatedOut };
        var updatedRecord = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(updatedRecord.CheckIn == new TimeOnly(9,40) && updatedRecord.CheckOut == new TimeOnly(18,0)
            && updatedRecord.WorkingHours == TimeSpan.FromMinutes(500), "Attendance screen uses corrected endpoints and duration");
        var updatedCsv = Encoding.UTF8.GetString(((FileContentResult)await controller.ExportAttendance(null, null, day, null)).FileContents);
        Check(updatedCsv.Contains("09:40") && updatedCsv.Contains("06:00") && updatedCsv.Contains("8h 20m"), "Daily Excel export uses updated times and duration");
        var updatedMonthly = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        var updatedXml = XDocument.Parse(Encoding.UTF8.GetString(updatedMonthly.FileContents));
        Check(updatedXml.Root!.Value.Contains("09:40") && updatedXml.Root!.Value.Contains("06:00") && updatedXml.Root!.Value.Contains("8h 20m"), "Monthly Excel export uses updated times and duration");
        stub.Logs = originalLogs;
        stub.LogReads = 0; stub.RosterReads = 0;
        var monthly = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        Check(stub.LogReads == 1 && stub.RosterReads == 1, "Monthly export loads punches and roster once for the entire month");
        var xml = XDocument.Parse(Encoding.UTF8.GetString(monthly.FileContents));
        XNamespace ns = "urn:schemas-microsoft-com:office:spreadsheet";
        var sheets = xml.Root!.Elements(ns + "Worksheet").ToList();
        Check(sheets.Count == 2 && sheets[1].Attribute(ns + "Name")!.Value == "Daily Details", "Monthly workbook includes daily details worksheet");
        var pivotRows = sheets[0].Descendants(ns + "Row").ToList();
        Check(pivotRows[2].Elements(ns + "Cell").First().Value == "00108" && sheets[1].Value.Contains("WDMS exact Name"), "Monthly summary and details use WDMS identity");
        Check(pivotRows[1].Elements(ns + "Cell").Count() == 38 && pivotRows[2].Elements(ns + "Cell").Count() == 38, "Monthly headers and data columns align");
        Check(pivotRows[2].Elements(ns + "Cell").ElementAt(7).Value == "1h 15m", "Monthly overtime sums daily excess without subtracting absent days");
        Check(sheets[1].Value.Contains("Site A & Works") && sheets[1].Value.Contains("Site B"), "Monthly details preserve site text and XML escaping");

        var originalEnrollments = stub.Enrollments;
        stub.Enrollments = originalEnrollments.Concat(new[] {
            new BiometricEnrollment { EnrollCode = "SECOND-108", EmployeeId = 1, Employee = employee, Status = "Linked", WdmsName = "Second WDMS code", LastSeenUtc = DateTime.UtcNow },
            new BiometricEnrollment { EnrollCode = "UNLINKED-9", Status = "Needs review", WdmsName = "Unlinked WDMS person", LastSeenUtc = DateTime.UtcNow }
        }).ToArray();
        var multipleCodes = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        var multipleXml = XDocument.Parse(Encoding.UTF8.GetString(multipleCodes.FileContents));
        Check(multipleXml.Root!.Value.Contains("SECOND-108") && multipleXml.Root.Value.Contains("UNLINKED-9"), "Monthly export supports multiple codes per employee and unlinked WDMS records without duplicate date crashes");
        var allCodesDay = Encoding.UTF8.GetString(((FileContentResult)await controller.ExportAttendance(null, null, day, null)).FileContents);
        Check(allCodesDay.Contains("SECOND-108") && allCodesDay.Contains("UNLINKED-9"), "Day export includes the same full roster as attendance screen");
        stub.Enrollments = originalEnrollments;

        Check(!new Employee().OvertimeEnabled && !new HrAddEmployeeViewModel().OvertimeEnabled && !new EmployeeFormViewModel().OvertimeEnabled, "New and existing employee forms require explicit overtime activation");
        var savedEmployee = stub.Enrollments[0].Employee;
        stub.Enrollments[0].Employee = null;
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single().Overtime.TotalMinutes == 75, "Linked employee settings resolve from ERP even when navigation data is missing");
        stub.Enrollments[0].Employee = savedEmployee;
        employee.OvertimeEnabled = false;
        var noOt = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(noOt.Overtime == TimeSpan.Zero && noOt.WorkingHours.TotalHours == 10, "Inactive overtime keeps actual worked hours but reports zero OT");
        var noOtCsv = Encoding.UTF8.GetString(((FileContentResult)await controller.ExportAttendance(null, null, day, null)).FileContents);
        Check(noOtCsv.Contains("0h 00m") && !noOtCsv.Contains("1h 15m"), "Export respects inactive overtime");
        employee.OvertimeEnabled = true;
        employee.ShiftStartTime = new TimeOnly(10, 0); employee.ShiftEndTime = new TimeOnly(19, 0);
        var assigned = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(!assigned.IsLate && assigned.Overtime.TotalHours == 1, "Assigned nine-hour shift changes overtime threshold");
        stub.Logs = new[] { Punch(9, 45, "Check In", "Site A"), Punch(19, 0, "Check Out", "Site A") };
        Check(!(await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single().IsLate, "Employee's 10 AM shift overrides global 09:30 late cutoff");
        employee.ShiftStartTime = new TimeOnly(22, 0); employee.ShiftEndTime = new TimeOnly(6, 0);
        var nextMorning = Punch(7, 0, "Check Out", "Site A"); nextMorning.PunchTime = nextMorning.PunchTime.AddDays(1);
        stub.Logs = new[] { Punch(22, 0, "Check In", "Site A"), nextMorning };
        var night = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(!night.IsLate && night.WorkingHours.TotalHours == 9 && night.Overtime.TotalHours == 1, "Night shift pairs next-day checkout and counts overtime");
        Check((await service.GetDailyAttendanceAsync(day.AddDays(1), null, null, null)).Records.Single().PunchCount == 0, "Night checkout is not counted again on the next shift");
        employee.OvertimeEnabled = false;
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single().Overtime == TimeSpan.Zero, "Night shift also respects inactive overtime");
        employee.OvertimeEnabled = true; employee.ShiftStartTime = new TimeOnly(9, 30); employee.ShiftEndTime = new TimeOnly(18, 30);
        stub.Logs = new[] { Punch(9, 0, "Check In", "Site A") };
        var open = (await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single();
        Check(open.OvertimeDisplay == "—" && open.SiteCheckOuts == "", "Open attendance does not invent checkout or overtime");

        var inactive = new Employee { Id = 2, EmployeeCode = "INACTIVE002", FullName = "Inactive Employee", Department = "Former Department", IsActive = false };
        var absent = new Employee { Id = 3, EmployeeCode = "E003", FullName = "Active Absent", Department = "Service" };
        stub.Employees = new[] { employee, absent };
        stub.Enrollments[1].Employee = inactive;
        stub.Enrollments[1].LastSeenUtc = DateTime.UtcNow;
        stub.Enrollments[2].Employee = absent;
        stub.Enrollments[2].LastSeenUtc = DateTime.UtcNow;
        stub.Logs = new[] {
            Punch(9, 0, "Check In", "Site A"),
            new AttendanceLog { EmployeeId = inactive.Id, Employee = inactive, DeviceUserId = "109", PunchTime = day.ToDateTime(new TimeOnly(11, 0)), PunchState = "IN" }
        };
        var history = await service.GetDailyAttendanceAsync(day, null, null, null);
        Check(history.Records.Count == 3 && history.Records.Single(x => x.EmployeeId == inactive.Id).PunchCount == 1, "Historical attendance retains inactive employee punches");
        var historyCsv = Encoding.UTF8.GetString(((FileContentResult)await controller.ExportAttendance(null, null, day, null)).FileContents);
        Check(historyCsv.Contains("Inactive Employee") && historyCsv.Contains("11:00 am", StringComparison.OrdinalIgnoreCase), "Daily Excel export preserves inactive employee punch time");
        var reportRange = await service.GetAttendanceRangeAsync(day, day.AddDays(2));
        Check(reportRange.Count(x => x.EmployeeId == inactive.Id) == 1 && reportRange.Where(x => x.EmployeeId == inactive.Id).All(x => x.PunchCount > 0 && x.Status != "Absent"), "Range report never invents inactive absences after last punch");
        Check(EmployeeAttendanceReader.BuildDays(inactive, day, day.AddDays(2), stub.Logs.Where(x=>x.EmployeeId==inactive.Id), []).Count == 1, "Individual inactive history contains real punch days only");

        var savedLogs = stub.Logs;
        var liveDate = DateOnly.FromDateTime(FieldAttendanceClock.Now);
        foreach(var log in stub.Logs) log.PunchTime = liveDate.ToDateTime(TimeOnly.FromDateTime(log.PunchTime));
        var roster = await service.GetDailyAttendanceAsync(liveDate, null, null, null);
        Check(roster.Records.Count == 2 && roster.Records.All(r => r.EmployeeId != inactive.Id), "Inactive employee with current punches is excluded from live attendance");
        Check(roster.PresentCount == 1 && roster.LateCount == 0 && roster.AbsentCount == (AttendanceRules.IsHoliday(liveDate) ? 0 : 1), "Live totals exclude inactive employees");
        Check(!roster.Departments.Contains("Former Department"), "Inactive department excluded from current attendance filters");
        Check((await service.GetDailyAttendanceAsync(liveDate, "109", null, null)).Records.Count == 0, "Inactive employee excluded from current search");
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, "Late")).Records.Count == 0, "Current late list excludes inactive punches");
        var categories = DashboardAttendanceCategories.Build(roster.Records, new Dictionary<int,string?> { [employee.Id] = EmployeeCategories.WhiteCollar, [inactive.Id] = EmployeeCategories.BlueCollar, [absent.Id] = EmployeeCategories.BlueCollar });
        Check(categories["Present"].BlueCollar == 0 && categories["Present"].WhiteCollar == 1, "Dashboard category totals exclude inactive punches");
        stub.ApprovedLeaveIds = new HashSet<int> { inactive.Id };
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, "Leave")).Records.Count == 0, "Current leave list excludes inactive employees");
        stub.ApprovedLeaveIds = new HashSet<int>();
        stub.Logs[1].VerificationMode = ManualAttendance.VerificationMode;
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.All(x=>x.EmployeeId!=inactive.Id), "Current corrected attendance cannot re-add inactive employee");
        stub.Logs[1].VerificationMode = "";
        var todayReport = await service.GetAttendanceReportAsync(liveDate, null, null, null);
        Check(todayReport.Records.Any(x=>x.EmployeeId==inactive.Id && x.PunchCount==1), "Today's report preserves punches even after employee is made inactive");
        var knownEmployees = stub.Employees;
        var knownCode = inactive.EmployeeCode;
        inactive.EmployeeCode = "BIO-109";
        stub.Employees = new[] { employee, absent, inactive };
        stub.Enrollments[1].EmployeeId = null;
        stub.Enrollments[1].Employee = null;
        stub.Enrollments[1].Status = "Needs review";
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.All(x => x.EmpId != "109"), "Inactive unlinked imported employee cannot appear as absent or present");
        inactive.IsActive = true;
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.Any(x => x.EmployeeId == inactive.Id), "Unlinked employee activation immediately restores current roster");
        inactive.IsActive = false;
        inactive.EmployeeCode = knownCode;
        stub.Enrollments[1].EmployeeId = inactive.Id;
        stub.Enrollments[1].Employee = inactive;
        stub.Enrollments[1].Status = "Linked";
        var newActive = new Employee { Id = 137, EmployeeCode = "NEW137", FullName = "Newly active employee", IsActive = true };
        stub.Employees = new[] { employee, absent, inactive, newActive };
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.Count(x => x.EmployeeId == 137) == 1, "Activated employee appears before WDMS sync");
        newActive.IsActive = false;
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.All(x => x.EmployeeId != 137), "Deactivated employee without WDMS disappears immediately");
        stub.Employees = knownEmployees;
        inactive.IsActive = true;
        Check((await service.GetDailyAttendanceAsync(liveDate, null, null, null)).Records.Any(x=>x.EmployeeId==inactive.Id), "Reactivated WDMS member returns to current attendance");
        inactive.IsActive = false;
        foreach(var log in savedLogs) log.PunchTime = day.ToDateTime(TimeOnly.FromDateTime(log.PunchTime));
        foreach (var exportMonth in new[] { "2026-09", DateTime.Today.ToString("yyyy-MM"), DateTime.Today.AddMonths(-1).ToString("yyyy-MM"), "2024-02" }.Distinct())
        {
            var monthFile = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: exportMonth);
            var monthXml = XDocument.Parse(Encoding.UTF8.GetString(monthFile.FileContents));
            Check(monthFile.FileDownloadName == $"Attendance-Pivot-{exportMonth}.xls" && monthXml.Root!.Elements(ns + "Worksheet").Count() == 2,
                $"Monthly export downloads valid workbook for {exportMonth}");
            if (exportMonth == "2026-09") Check(monthXml.Root!.Value.Contains("Inactive Employee"), "Monthly Excel retains inactive employee punch history");
        }
        stub.Enrollments[1].LastSeenUtc = null;
        Check((await service.GetAttendanceReportAsync(day, null, null, null)).Records.Any(x=>x.EmployeeId==inactive.Id && x.PunchCount==1), "Inactive punch history survives a missing WDMS directory entry");
        // ERP activation and device mappings cannot add a person outside the WDMS roster.
        inactive.IsActive = true;
        stub.Employees = new[] { employee, absent, inactive };
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.All(r => r.EmpId != "109"), "ERP reactivation cannot add a non-WDMS member");
        foreach (var holidayDate in new[] { new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 4) })
        {
            var holiday = await service.GetDailyAttendanceAsync(holidayDate, null, null, null);
            Check(holiday.AbsentCount == 0 && holiday.Records.All(x => x.Status == "Holiday"), "Holiday roster excludes absences: " + holidayDate);
            Check((await service.GetDailyAttendanceAsync(holidayDate, null, null, "Absent")).Records.Count == 0, "Absent filter excludes holidays");
            Check((await service.GetDailyAttendanceAsync(holidayDate, null, null, "Holiday")).Records.Count == 2, "Holiday filter returns employees");
            var holidayCsv = Encoding.UTF8.GetString(((FileContentResult)await controller.ExportAttendance(null, null, holidayDate, null)).FileContents);
            Check(holidayCsv.Contains("\"Holiday\"") && !holidayCsv.Contains("\"Absent\""), "Daily export labels holidays");
        }
        stub.ApprovedLeaveIds = new HashSet<int> { 1, 3 };
        var onLeave = await service.GetDailyAttendanceAsync(day, null, null, null);
        Check(onLeave.Records.Single(x => x.EmployeeId == 1).Status == "Present", "Punches take priority over approved leave");
        Check(onLeave.Records.Single(x => x.EmployeeId == 3).Status == "Leave" && onLeave.LeaveCount == 1 && onLeave.AbsentCount == 0, "Approved working-day leave is not absent");
        Check((await service.GetDailyAttendanceAsync(new DateOnly(2026, 9, 6), null, null, null)).Records.All(x => x.Status == "Holiday"), "Sunday takes priority over approved leave");
        stub.ApprovedLeaveIds = new HashSet<int>();
        Check((await service.GetDailyAttendanceAsync(day, null, null, null)).Records.Single(x => x.EmployeeId == 3).Status == "Absent", "Without approved leave working-day absence remains Absent");
        var holidayWorkbook = (FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-09");
        var holidayXml = XDocument.Parse(Encoding.UTF8.GetString(holidayWorkbook.FileContents));
        var holidayCells = holidayXml.Descendants(ns + "Cell").Where(x => (string?)x.Attribute(ns + "StyleID") == "Holiday").ToList();
        Check(holidayCells.Count > 0 && holidayCells.All(x => x.Value.StartsWith("Holiday")), "Monthly export uses Holiday status and style");
    }
}

public class ExportRepository : DispatchProxy
{
    public int LogReads;
    public int RosterReads;
    public IReadOnlySet<int> ApprovedLeaveIds = new HashSet<int>();
    public IReadOnlyList<Employee> Employees = Array.Empty<Employee>();
    public IReadOnlyList<AttendanceLog> Logs = Array.Empty<AttendanceLog>();
    public IReadOnlyList<BiometricEnrollment> Enrollments = Array.Empty<BiometricEnrollment>();
    public IReadOnlyList<EmployeeDeviceMapping> Mappings = Array.Empty<EmployeeDeviceMapping>();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "GetAttendanceLogsAsync") LogReads++;
        if (method.Name == "GetEnrollmentsAsync") RosterReads++;
        return method.Name switch
    {
        "GetApprovedLeaveEmployeeIdsAsync" => Task.FromResult(ApprovedLeaveIds),
        "GetActiveEmployeesAsync" => Task.FromResult(Employees),
        "GetAttendanceEmployeesAsync" => Task.FromResult(Employees),
        "GetEnrollmentsAsync" => Task.FromResult(Enrollments),
        "GetActiveMappingsAsync" => Task.FromResult(Mappings),
        "GetAttendanceLogsAsync" => Task.FromResult<IReadOnlyList<AttendanceLog>>(Logs.Where(log => log.PunchTime >= (DateTime)args![0]! && log.PunchTime < (DateTime)args[1]!).ToList()),
        _ => throw new NotSupportedException(method.Name)
    };
    }
}
