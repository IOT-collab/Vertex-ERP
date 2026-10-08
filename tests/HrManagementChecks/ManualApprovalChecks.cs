using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;

static class ManualApprovalChecks
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    public static async Task Run(string settingsPath)
    {
        var review = typeof(AttendanceRequestsController).GetMethod("Review")!;
        Check(review.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(a => a.Roles == "Admin"), "Only Admin may review");
        Check(review.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), "Review requires antiforgery token");
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "manual_approval_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(cs.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            cs.SearchPath = schema;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            var employee = new Employee { EmployeeCode = "MANUAL", FirstName = "Test", FullName = "Test", Email = "test@example.invalid", PhoneNumber = "123", Department = "Test", Designation = "Test" };
            db.Employees.Add(employee); await db.SaveChangesAsync();
            var user = new AppUser { Username = "TEST", NormalizedUsername = "TEST", Role = "Employee", EmployeeId = employee.Id, FullName = "Test" };
            db.AppUsers.Add(user); await db.SaveChangesAsync();
            ControllerContext Context(string role, int id) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test")) } };
            var hrContext = Context("HR", user.Id);
            var hr = new MainController(db, null!, null!, null!, null!, null!) { ControllerContext = hrContext, TempData = new TempDataDictionary(hrContext.HttpContext, new Temp()) };
            await hr.AddAttendance(new ManualAttendanceViewModel { EmployeeId = employee.Id, Department = "Test", AttendanceDate = new DateOnly(2026, 9, 1), CheckInTime = new TimeOnly(9, 0), CheckOutTime = new TimeOnly(18, 0), Remarks = "Thumb not recognized" });
            var request = await db.AttendanceRequests.SingleAsync();
            Check(request.Status == "Pending" && !await db.AttendanceLogs.AnyAsync(), "HR submission remains pending without marking attendance");
            var context = Context("Admin", 999);
            var controller = new AttendanceRequestsController(db) { ControllerContext = context, TempData = new TempDataDictionary(context.HttpContext, new Temp()) };
            await controller.Review(request.Id, "Approved", "Verified");
            Check(request.Status == "Approved" && request.ReviewedByUserId == 999 && await db.AttendanceLogs.CountAsync() == 2, "Approval persists reviewer and check-in/out");
            Check(await db.AttendanceLogs.AllAsync(l => l.EmployeeId == employee.Id && l.VerificationMode == "Manual Approved"), "Approved punches feed employee attendance");
            await controller.Review(request.Id, "Approved", null);
            Check(await db.AttendanceLogs.CountAsync() == 2, "Repeated approval cannot duplicate attendance");
            var rejected = new AttendanceRequest { EmployeeId = employee.Id, AttendanceDate = new DateOnly(2026, 9, 2), CheckInTime = new TimeOnly(9, 0), Reason = "Device offline", RequestedByUserId = user.Id };
            db.AttendanceRequests.Add(rejected); await db.SaveChangesAsync();
            await controller.Review(rejected.Id, "Rejected", " ");
            Check(rejected.Status == "Pending", "Rejection requires reason");
            await controller.Review(rejected.Id, "Rejected", "Not verified");
            Check(rejected.Status == "Rejected" && rejected.ReviewReason == "Not verified" && await db.AttendanceLogs.CountAsync() == 2, "Rejection stores reason without attendance");
            var employeeContext = Context("Employee", user.Id);
            var employeeController = new AttendanceRequestsController(db) { ControllerContext = employeeContext };
            Check(await employeeController.Index() is ForbidResult, "Employee cannot access manual attendance requests");
            employeeController.ControllerContext = Context("Employee", 0);
            Check(await employeeController.Index() is ForbidResult, "Unlinked employee cannot access manual attendance requests");
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
    }
    sealed class Temp : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
