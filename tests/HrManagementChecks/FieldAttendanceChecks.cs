using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class FieldAttendanceChecks
{
    public static async Task Run(string? settingsPath, Action<bool, string> check)
    {
        check(FieldAttendanceClock.IndiaTime(DateTimeOffset.Parse("2026-09-21T20:00:00Z")) == new DateTime(2026, 9, 22, 1, 30, 0), "Field attendance uses India date on UTC hosts");
        var request = Request("Check In");
        request.SiteName = " ";
        check(!Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true), "Blank field site rejected");
        request.SiteName = new string('x', 161);
        check(!Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true), "Overlong field site rejected");
        var validationController = Controller(null!, "Employee", 0);
        request = Request("Check In"); request.AccuracyMetres = 51;
        check(await validationController.SubmitFieldAttendance(request) is BadRequestObjectResult, "Imprecise GPS cannot save attendance");
        request = Request("Check In"); request.CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3);
        check(await validationController.SubmitFieldAttendance(request) is BadRequestObjectResult, "Expired GPS cannot save attendance");
        request = Request("Check In"); request.Latitude = null;
        check(await validationController.SubmitFieldAttendance(request) is BadRequestObjectResult, "Missing GPS cannot save attendance");
        if (settingsPath == null) return;

        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "field_check_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            // Exercise the actual additive migration against the previous table shape.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AttendanceLogs\" DROP COLUMN \"FieldSiteName\", DROP COLUMN \"LocationCapturedAtUtc\"");
            var migrations = db.GetService<IMigrationsAssembly>();
            var migrationType = migrations.Migrations.Single(m => m.Key.EndsWith("_AddFieldAttendanceSiteDetails")).Value;
            var migration = migrations.CreateMigration(migrationType, db.Database.ProviderName!);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            check(await db.AttendanceLogs.CountAsync() == 0, "Site-details migration upgrades the previous attendance table");
            Employee Employee(string code) => new() { EmployeeCode = code, FirstName = code, FullName = code, Email = code + "@example.invalid", PhoneNumber = code, Department = "Test", Designation = "Test" };
            var manager = Employee("MANAGER"); var member = Employee("MEMBER"); var outsider = Employee("OTHER");
            member.ReportingManager = manager;
            db.Employees.AddRange(manager, member, outsider);
            await db.SaveChangesAsync();
            AppUser User(Employee e, string role) => new() { EmployeeId = e.Id, Username = e.EmployeeCode, NormalizedUsername = e.EmployeeCode, FullName = e.FullName, PasswordHash = "unused", Role = role };
            var managerUser = User(manager, "Manager"); var memberUser = User(member, "Employee");
            db.AppUsers.AddRange(managerUser, memberUser);
            await db.SaveChangesAsync();
            var employeeController = Controller(db, "Employee", memberUser.Id);
            check(await employeeController.SubmitFieldAttendance(Request("Check Out")) is BadRequestObjectResult, "Check-out requires check-in");
            check(await employeeController.SubmitFieldAttendance(Request("Check In")) is OkObjectResult, "Field check-in saves successfully");
            db.ChangeTracker.Clear();
            var saved = await db.AttendanceLogs.SingleAsync();
            check(saved.EmployeeId == member.Id && saved.FieldSiteName == "Customer Site A" && saved.Latitude == 28.6139m && saved.Longitude == 77.2090m && saved.LocationCapturedAtUtc.HasValue, "Database retains employee, site, GPS and capture time");
            check(Math.Abs((saved.PunchTime - FieldAttendanceClock.Now).TotalSeconds) < 30, "Saved punch uses server-generated India time");
            check(await employeeController.SubmitFieldAttendance(Request("Check In")) is ConflictObjectResult, "Duplicate check-in rejected");
            var checkout = Request("Check Out"); checkout.SiteName = "Customer Site B"; checkout.Latitude = 28.6200m;
            check(await employeeController.SubmitFieldAttendance(checkout) is OkObjectResult, "Check-out records its own site and location");
            var today = DateOnly.FromDateTime(FieldAttendanceClock.Now);
            var managerView = (SiteEmployeeLocationViewModel)((ViewResult)await Controller(db, "Manager", managerUser.Id).LocationTracking(today, outsider.Id)).Model!;
            check(managerView.IsManagerView && managerView.Employees.Count == 1 && managerView.Employees[0].EmployeeId == member.Id, "Manager cannot access another team via employeeId");
            check(managerView.SelectedEmployee?.CheckOutSiteName == "Customer Site B" && managerView.SelectedDate == today, "Manager sees saved sites and selected date");
            foreach (var role in new[] { "HR", "Admin" })
            {
                var view = (SiteEmployeeLocationViewModel)((ViewResult)await Controller(db, role, 0).LocationTracking(today, member.Id)).Model!;
                check(view.Employees.Count == 3 && view.SelectedEmployee?.CheckInSiteName == "Customer Site A", role + " can see all employees and recorded site");
            }
            check(await Controller(db, "Manager", 0).LocationTracking(today, member.Id) is ForbidResult, "Unlinked manager receives no employee locations");
            var previous = (SiteEmployeeLocationViewModel)((ViewResult)await Controller(db, "HR", 0).LocationTracking(today.AddDays(-1), member.Id)).Model!;
            check(previous.Employees.All(e => !e.CheckInTime.HasValue), "Date filter excludes other days");
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }

    static FieldAttendanceRequest Request(string action) => new() { Action = action, SiteName = " Customer Site A ", Latitude = 28.6139m, Longitude = 77.2090m, AccuracyMetres = 10, CapturedAtUtc = DateTimeOffset.UtcNow };
    static MainController Controller(ApplicationDbContext db, string role, int userId) => new(db, null!, null!, null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role) }, "test")) } }
    };
}
