using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using VertexERP.Models;
using VertexERP.Repositories;
using VertexERP.Services;
var repo=DispatchProxy.Create<IBiometricRepository, RosterRepository>();
var fake=(RosterRepository)(object)repo;
var date=new DateOnly(2026,10,14);
var employee=new Employee { Id=1, FullName="Biometric User 001", Department="Engineering" };
fake.Enrollments=[
 new(){EnrollCode="001",WdmsName="WDMS Actual Name",LastSeenUtc=DateTime.UtcNow,EmployeeId=1,Employee=employee,Status="Linked"},
 new(){EnrollCode="A-42",WdmsName="Unlinked Name",LastSeenUtc=DateTime.UtcNow,Status="Needs review"},
 new(){EnrollCode="3",LastSeenUtc=DateTime.UtcNow,Status="Needs review"},
 new(){EnrollCode="4",WdmsName="Removed from WDMS"},
 new(){EnrollCode="5",FullName="Sheet-only Person",IsSheetReference=true}
];
fake.Logs=[
 new(){DeviceUserId="001",PunchTime=date.ToDateTime(new TimeOnly(9,0)),PunchState="0"},
 new(){DeviceUserId="A-42",PunchTime=date.ToDateTime(new TimeOnly(9,10)),PunchState="0"},
 new(){DeviceUserId="ERP001",EmployeeId=1,VerificationMode="Manual Approved",PunchTime=date.ToDateTime(new TimeOnly(18,0)),PunchState="Check Out"},
 new(){DeviceUserId="999",EmployeeId=99,PunchTime=date.ToDateTime(new TimeOnly(9,0)),PunchState="0"}
];
var service=new AttendanceProcessingService(repo,Options.Create(new AttendanceOptions()),NullLogger<AttendanceProcessingService>.Instance);
var page=await service.GetDailyAttendanceAsync(date,null,null,null);
void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS: "+name);}
Check(page.Records.Count==3,"Only WDMS members, including unlinked and no-punch users");
var linked=page.Records.Single(x=>x.EmpId=="001");
Check(linked.EmployeeName=="WDMS Actual Name" && linked.Department=="Engineering","WDMS name replaces ERP placeholder; ERP department retained");
Check(linked.PunchCount==2 && linked.CheckOut==new TimeOnly(18,0),"Unique verified link includes manual attendance");
Check(page.Records.Single(x=>x.EmpId=="A-42").PunchCount==1,"Unlinked alphanumeric enrollment retains its punch");
Check(page.Records.Single(x=>x.EmpId=="3").EmployeeName=="Name unavailable in WDMS","Missing source name does not invent a biometric name");
Check(page.PresentCount==2 && page.AbsentCount==1,"Counts use WDMS roster");
Check((await service.GetDailyAttendanceAsync(date,"001",null,null)).Records.Count==1,"Search preserves original leading-zero enrollment");
Check((await service.GetDailyAttendanceAsync(date,null,"Engineering",null)).Records.Count==1,"Department filter");
Check((await service.GetDailyAttendanceAsync(date,null,null,"Present")).Records.Count==2,"Present filter");
fake.Enrollments=[];
Check((await service.GetDailyAttendanceAsync(date,null,null,null)).Records.Count==0,"Empty WDMS roster never falls back to ERP profiles");
// Manual attendance is an explicit exception to the WDMS roster, rather than an ERP roster fallback.
var manualEmployee = new Employee { Id = 77, EmployeeCode = "ERP77", FullName = "Manual Employee", Department = "Office" };
fake.Logs = [new() { EmployeeId = 77, Employee = manualEmployee, DeviceUserId = "ERP77", VerificationMode = "Manual Approved", PunchTime = date.ToDateTime(new(9,45)), PunchState = "Check In", RawPayload = "Manual attendance | Marked by HR | Reason: Missed punch" },
    new() { EmployeeId = 77, Employee = manualEmployee, DeviceUserId = "ERP77", VerificationMode = "Manual Approved", PunchTime = date.ToDateTime(new(18,30)), PunchState = "Check Out", RawPayload = "Manual attendance | Marked by HR | Reason: Missed punch" }];
var manualPage = await service.GetDailyAttendanceAsync(date, null, null, null);
var manualRecord = manualPage.Records.Single();
Check(manualPage.PresentCount == 1 && manualRecord.Status == "Present" && manualRecord.EmployeeId == 77, "HR manual employee without WDMS appears in Present count");
Check(manualRecord.WorkingHours.TotalMinutes == 525 && manualRecord.TotalHoursDisplay == "8h 45m", "09:45 to 18:30 calculates actual 8h45 duration");
Check(!manualRecord.Remark.Contains("Manual attendance") && !manualRecord.Remark.Contains("Missed punch"), "Attendance report omits correction source and reason");
Check((await service.GetDailyAttendanceAsync(date, "ERP77", null, "Present")).Records.Count == 1, "Manual employee searchable and included by Present filter");
var own = EmployeeAttendanceReader.BuildDays(manualEmployee, date, date, fake.Logs, [] ).Single();
Check(own.Source == "Punch" && own.WorkingHours.TotalMinutes == 525 && own.Status == "Present", "Employee login shows same manual times and duration");
Check(!AttendanceRules.IsLateArrival(date.ToDateTime(new(9,45))) && AttendanceRules.IsLateArrival(date.ToDateTime(new(9,45)).AddSeconds(1)), "Office exactly 09:45 is on time; later is late");
var plan = ManualAttendance.Plan(date, new(9,45), new(18,30), []);
Check(plan.Error == null && plan.Punches.Count == 2, "Missing attendance creates two boundaries");
var existingIn = new AttendanceLog { PunchTime = date.ToDateTime(new(9,45)) };
plan = ManualAttendance.Plan(date, new(9,45), new(18,30), [existingIn]);
Check(plan.Error == null && plan.Punches.Count == 1 && plan.Punches.Single().State == "Check Out", "Missed checkout adds only missing endpoint");
var existingOut = new AttendanceLog { PunchTime = date.ToDateTime(new(18,30)) };
plan = ManualAttendance.Plan(date, new(9,45), new(18,30), [existingOut]);
Check(plan.Punches.Count == 1 && plan.Punches.Single().State == "Check In", "Missed checkin adds only missing endpoint");
plan = ManualAttendance.Plan(date, new(9,45), new(18,30), [existingIn, existingOut]);
Check(plan.Error == null && plan.Punches.Count == 0, "Existing boundaries are not duplicated");
Check(ManualAttendance.Plan(date, new(10,0), new(18,30), [existingIn]).Error != null, "Manual times cannot erase earlier biometric evidence");
Check(ManualAttendance.Plan(date, new(9,45), new(18,0), [existingOut]).Error != null, "Manual times cannot erase later biometric evidence");
Check(ManualAttendance.Plan(date, new(18,30), new(9,45), []).Error != null, "Reversed times rejected");
fake.Logs[1].PunchTime = date.ToDateTime(new(17,30));
var early = (await service.GetDailyAttendanceAsync(date, null, null, null)).Records.Single();
Check(early.WorkingHours.TotalMinutes == 465 && early.Remark.Contains("Early exit"), "HR's earlier checkout changes duration and shows Early exit");
var controller = new VertexERP.Controllers.MainController(null!, service, null!, null!, null!, null!);
var csv = (Microsoft.AspNetCore.Mvc.FileContentResult)await controller.ExportAttendance(null, null, date, null);
var csvText = System.Text.Encoding.UTF8.GetString(csv.FileContents);
Check(csvText.Contains("Manual Employee") && csvText.Contains("7h 45m") && !csvText.Contains("Missed punch") && !csvText.Contains("Marked by"), "Excel-compatible daily export includes manual employee, actual duration without correction details");
var monthlyFile = (Microsoft.AspNetCore.Mvc.FileContentResult)await controller.ExportAttendance(null, null, null, null, exportPeriod: "month", exportMonth: "2026-10");
var workbook = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(monthlyFile.FileContents));
Check(workbook.ToString().Contains("Manual Employee") && !workbook.ToString().Contains("Missed punch") && !workbook.ToString().Contains("Manual Entry Details") && workbook.ToString().Contains("7h 45m"), "Monthly Excel workbook includes actual attendance without correction details");
manualEmployee.ShiftStartTime = new(11,0); manualEmployee.ShiftEndTime = new(19,0);
fake.Logs[0].PunchTime = date.ToDateTime(new(11,5)); fake.Logs[1].PunchTime = date.ToDateTime(new(19,0));
var shifted = (await service.GetDailyAttendanceAsync(date, null, null, null)).Records.Single();
Check(shifted.IsLate && shifted.LateBy.TotalMinutes == 5 && shifted.WorkingHours.TotalMinutes == 475, "Assigned 11 AM shift determines five-minute lateness and actual hours");
Check(!AttendanceRules.IsLateArrival(date.ToDateTime(new(11,0)), date, new(11,0)), "Exactly assigned 11 AM start is on time");
var nightPlan = ManualAttendance.Plan(date, new(22,0), new(6,0), [], new(22,0), new(6,0));
Check(nightPlan.Error == null && nightPlan.Punches.Last().Time == date.AddDays(1).ToDateTime(new(6,0)), "Manual night shift checkout belongs to following morning");
var afterMidnight = ManualAttendance.Plan(date, new(0,30), new(6,0), [], new(22,0), new(6,0));
Check(afterMidnight.Error == null && afterMidnight.Punches.First().Time.Date == date.AddDays(1).ToDateTime(TimeOnly.MinValue) && (afterMidnight.Punches.Last().Time-afterMidnight.Punches.First().Time).TotalMinutes == 330, "After-midnight arrival remains in the selected night shift");
manualEmployee.ShiftStartTime = new(22,0); manualEmployee.ShiftEndTime = new(6,0);
fake.Logs[0].PunchTime = date.AddDays(1).ToDateTime(new(0,30)); fake.Logs[1].PunchTime = date.AddDays(1).ToDateTime(new(6,0));
var nightRecord = (await service.GetDailyAttendanceAsync(date, null, null, null)).Records.Single();
Check(nightRecord.IsLate && nightRecord.LateBy.TotalMinutes == 150 && nightRecord.WorkingHours.TotalMinutes == 330, "Night-shift late duration and worked hours use actual next-day times");
foreach (var shift in new[] { new TimeOnly(9,30), new TimeOnly(9,45), new TimeOnly(10,0), new TimeOnly(22,0) })
{
    var cutoff = shift == new TimeOnly(9,30) ? new TimeOnly(9,45) : shift;
    Check(!AttendanceRules.IsLateArrival(date.ToDateTime(cutoff), date, shift) && AttendanceRules.IsLateArrival(date.ToDateTime(cutoff).AddSeconds(1), date, shift), "Assigned cutoff respects the 09:30 office allowance and exact custom shift starts: " + shift);
}
public class RosterRepository:DispatchProxy {
 public List<BiometricEnrollment> Enrollments=[];
 public List<AttendanceLog> Logs=[];
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {
 "GetEnrollmentsAsync"=>Task.FromResult<IReadOnlyList<BiometricEnrollment>>(Enrollments),
 "GetAttendanceLogsAsync"=>Task.FromResult<IReadOnlyList<AttendanceLog>>(Logs),
 "GetApprovedLeaveEmployeeIdsAsync"=>Task.FromResult<IReadOnlySet<int>>(new HashSet<int>()),
 _=>throw new NotSupportedException(method.Name)
 };
}
