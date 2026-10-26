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
