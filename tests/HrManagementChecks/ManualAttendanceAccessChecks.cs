using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

internal static class ManualAttendanceAccessChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); passed++; Console.WriteLine("PASS " + text); }
        var employee = new Employee { Id = 7, FullName = "Yuvraj Singh", CanManageManualAttendance = true };
        var account = new AppUser { EmployeeId = 7, Role = "Employee" };
        Check(ManualAttendanceAccess.Allows(account, employee), "Designated employee may directly mark attendance");
        employee.FullName = "New saved name";
        Check(ManualAttendanceAccess.Allows(account, employee), "Name changes do not revoke designated permission");
        account.Role = "HR";
        Check(!ManualAttendanceAccess.Allows(account, employee), "HR is denied even if a permission flag exists");
        account.Role = "Employee"; employee.CanManageManualAttendance = false;
        Check(!ManualAttendanceAccess.Allows(account, employee), "Ordinary employees are denied");
        account.Role = "Admin";
        Check(!ManualAttendanceAccess.Allows(account, employee), "Admin role alone cannot bypass selected-employee permission");
        account.Role = "Manager";
        Check(!ManualAttendanceAccess.Allows(account, employee), "Manager role alone cannot mark attendance");
        employee.CanManageManualAttendance = true; employee.IsActive = false;
        Check(!ManualAttendanceAccess.Allows(account, employee), "Inactive employee is denied");
        employee.IsActive = true; account.IsActive = false;
        Check(!ManualAttendanceAccess.Allows(account, employee), "Inactive account is denied");
        account.IsActive = true; account.EmployeeId = 8;
        Check(!ManualAttendanceAccess.Allows(account, employee), "Permission requires exact employee/login linkage");
        foreach (var method in typeof(MainController).GetMethods().Where(m => m.Name is "AddAttendance" or "ManualAttendanceTimes"))
            Check(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(a => a.Policy == ManualAttendanceAccess.Policy), method.Name + " enforces named operator policy");
        var post = typeof(MainController).GetMethods().Single(m => m.Name == "AddAttendance" && m.GetParameters().Length == 1);
        Check(post.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), "Attendance mutation retains antiforgery protection");
        var controller = new MainController(null!, null!, null!, null!, null!, null!) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "HR"), new Claim(ClaimTypes.NameIdentifier, "1") }, "test"))
            } }
        };
        Check(await controller.AddAttendance() is ForbidResult, "HR direct GET is forbidden");
        Check(await controller.AddAttendance(new ManualAttendanceViewModel()) is ForbidResult, "HR direct POST is forbidden");
        Check(await controller.ManualAttendanceTimes(1, new DateOnly(2026,10,27)) is ForbidResult, "HR cannot bypass through punch-time lookup");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261027113000_SynchronizeEmployeeMasterLabels", "RestrictManualAttendanceOperators");
        Check(sql.Contains("yuvraj singh") && sql.Contains("deepa katariya") && sql.Contains("HAVING count(*) = 1"), "Migration only grants uniquely matched intended employees");
        Check(sql.Contains("JOIN \"Users\" account") && sql.Contains("account.\"EmployeeId\" = employee.\"Id\""), "Permission grants require active linked login using mapped Users table");
        Check(!new Employee().CanManageManualAttendance, "New employees receive no manual attendance access by default");
        var latest = db.GetService<IMigrator>().GenerateScript("20261027120000_RestrictManualAttendanceOperators", "20261027130000_KeepOnlyYuvrajManualAttendance");
        Check(latest.Contains("yuvraj singh") && !latest.Contains("deepa katariya") && latest.Contains("SET \"CanManageManualAttendance\" = EXISTS"), "Latest migration restricts permission to uniquely identified Yuvraj and revokes others");
        var punch = new AttendanceLog { WorkCode = ManualAttendance.CorrectionCode, VerificationMode = ManualAttendance.VerificationMode };
        db.AttendanceLogs.Add(punch);
        Check(AuditLogFactory.Create(db.Entry(punch), null) == null, "Manual attendance creates no separate audit entry");
        Check(ManualAttendance.Details(new[] { punch }) == "", "Manual attendance adds no actor/time history to employee views or exports");
        var device = new BiometricDevice { SerialNumber = "MANUAL-ENTRY" };
        db.BiometricDevices.Add(device);
        Check(AuditLogFactory.Create(db.Entry(device), null) == null, "Manual device setup creates no audit entry");
        var regularPunch = new AttendanceLog(); db.AttendanceLogs.Add(regularPunch);
        Check(AuditLogFactory.Create(db.Entry(regularPunch), null) != null, "Other attendance activity retains normal audit behavior");
        Console.WriteLine($"{passed} manual attendance access checks passed.");
    }
}
