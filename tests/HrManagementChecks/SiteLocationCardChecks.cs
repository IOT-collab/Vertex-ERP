using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
internal static class SiteLocationCardChecks
{
    public static async Task Run()
    {
        var day=new DateOnly(2026,10,29);var start=day.ToDateTime(TimeOnly.MinValue);var end=start.AddDays(1);var passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        var employees=new[]{new Employee{Id=1,FullName="Field Employee",ReportingManagerId=7},new Employee{Id=2,FullName="Office Employee",ReportingManagerId=7},new Employee{Id=3,FullName="Other Team",ReportingManagerId=8},new Employee{Id=4,FullName="Inactive",IsActive=false}}.AsQueryable();
        var logs=new[]{new AttendanceLog{Id=1,EmployeeId=1,PunchTime=start.AddHours(9),PunchState="IN",VerificationMode=AttendanceRules.FieldVerificationMode,FieldSiteName="Site A",Latitude=28,Longitude=77},new AttendanceLog{Id=2,EmployeeId=2,PunchTime=start.AddHours(9),BiometricDevice=new BiometricDevice{CommunicationMode="Push"}},new AttendanceLog{Id=3,EmployeeId=3,PunchTime=start.AddHours(10),VerificationMode=AttendanceRules.FieldVerificationMode},new AttendanceLog{Id=4,EmployeeId=4,PunchTime=start.AddHours(10),VerificationMode=AttendanceRules.FieldVerificationMode}};
        var active=employees.Where(x=>x.IsActive);
        Check(SiteLocationCards.WithAttendance(active,logs.AsQueryable(),start,end).Select(x=>x.Id).SequenceEqual(new[]{1,3}),"Only active employees with selected-date site attendance appear");
        Check(SiteLocationCards.WithAttendance(active.Where(x=>x.ReportingManagerId==7),logs.AsQueryable(),start,end).Select(x=>x.Id).SequenceEqual(new[]{1}),"Manager scope remains restricted to assigned team");
        Check(!SiteLocationCards.WithAttendance(active,logs.AsQueryable(),end,end.AddDays(1)).Any(),"Other dates do not show stale cards");
        var profile=new SiteEmployeeLocationItem{EmployeeId=1,EmployeeName="Field Employee",PhotoPath="/profile.jpg"};
        var first=SiteLocationCards.Build(profile,logs);Check(first.CheckInTime==start.AddHours(9)&&first.PhotoPath=="/profile.jpg"&&!first.IsCheckedOut,"Card preserves profile photo and initial check-in");
        var checkout=new AttendanceLog{Id=5,EmployeeId=1,PunchTime=start.AddHours(18),PunchState="OUT",FieldSiteName="Site B",SelfiePath="/selfie.jpg"};
        var updated=SiteLocationCards.Build(profile,logs.Append(checkout));Check(updated.EmployeeId==first.EmployeeId&&updated.IsCheckedOut&&updated.CheckOutTime==checkout.PunchTime&&updated.CheckOutSelfiePath=="/selfie.jpg","Checkout updates the same employee card and selfie");
        var returnToSite=new AttendanceLog{Id=6,EmployeeId=1,PunchTime=start.AddHours(19),PunchState="Check In",FieldSiteName="Site C"};
        var returned=SiteLocationCards.Build(profile,logs.Append(checkout).Append(returnToSite));Check(!returned.IsCheckedOut&&returned.LastSiteName=="Site C"&&returned.LastLocationTime==returnToSite.PunchTime,"Later re-entry shows latest site instead of stale checkout state");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options);
        var sql=SiteLocationCards.WithAttendance(db.Employees.Where(x=>x.IsActive),db.AttendanceLogs,start,end).ToQueryString();Check(sql.Contains("EXISTS")&&sql.Contains("VerificationMode")&&sql.Contains("PunchTime"),"Filtered roster translates to PostgreSQL before loading employees");
        await SiteLocationCardViewChecks.Run();Console.WriteLine($"{passed} site card checks and page renders passed.");
    }
}
