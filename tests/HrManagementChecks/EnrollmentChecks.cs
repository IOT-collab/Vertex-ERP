using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class EnrollmentChecks
{
    public static async Task Run(string settingsPath)
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "enrollment_check_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString, x => x.EnableRetryOnFailure()).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"BiometricEnrollments\"");
            var assembly = db.GetService<IMigrationsAssembly>();
            var migration = assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_AddWdmsEnrollments")).Value, db.Database.ProviderName!);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            Check(await db.BiometricEnrollments.CountAsync() == 64 && await db.BiometricEnrollments.AnyAsync(x => x.EnrollCode == "5" && x.FullName == "Amit Kumar Bhati"), "Migration seeds all 64 sheet enroll-code/name pairs");
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"BiometricEnrollments\"");
            Employee Employee(string code, string name, bool pending = false) => new() { EmployeeCode = code, FullName = name, FirstName = name, Email = code + "@example.invalid", PhoneNumber = code, Department = "Test", Designation = "Test", IsBiometricProfilePending = pending };
            var alice = Employee("ERP-A", "Alice Example"); var bob = Employee("ERP-B", "Bob Example"); var placeholder = Employee("ERP-C", "Biometric User 9", true);
            var device = new BiometricDevice { Name = "Test", SerialNumber = "ENROLL-TEST", Model = "Test" };
            db.AddRange(alice, bob, placeholder, device); await db.SaveChangesAsync();
            db.EmployeeDeviceMappings.AddRange(new EmployeeDeviceMapping { Employee = alice, BiometricDevice = device, DeviceUserId = "7" }, new EmployeeDeviceMapping { Employee = bob, BiometricDevice = device, DeviceUserId = "5" }, new EmployeeDeviceMapping { Employee = placeholder, BiometricDevice = device, DeviceUserId = "9" });
            db.BiometricEnrollments.AddRange(new BiometricEnrollment { EnrollCode = "7", FullName = "Alice Example", IsSheetReference = true }, new BiometricEnrollment { EnrollCode = "5", FullName = "Different Person", IsSheetReference = true });
            db.BiometricEmployeeExclusions.Add(new BiometricEmployeeExclusion { DeviceUserCode = "22" });
            var punchIndex = 0;
            AttendanceLog Punch(string code, int? employee = null) => new() { BiometricDeviceId = device.Id, EmployeeId = employee, DeviceUserId = code, PunchTime = DateTime.SpecifyKind(DateTime.Today.AddHours(9).AddMinutes(punchIndex++), DateTimeKind.Unspecified), UniqueHash = Guid.NewGuid().ToString("N"), RawPayload = "TEST" };
            var historical = Punch("5", bob.Id); db.AttendanceLogs.Add(historical); await db.SaveChangesAsync();
            EnrollmentIdentity[] identities = [new("7", "Alice WDMS"), new("5", "Different Person"), new("9", "Named New Hire"), new("008", "Leading Zero"), new("22", "Deleted Person")];
            await BiometricEnrollmentService.SyncAsync(db, identities);
            Check((await db.BiometricEnrollments.FindAsync("7"))!.EmployeeId == alice.Id, "Reference name resolves existing employee and overrides WDMS spelling");
            Check((await db.BiometricEnrollments.FindAsync("5"))!.Status == "Needs review", "Conflicting existing employee is not reassigned");
            Check((await db.Employees.FindAsync(placeholder.Id))!.FullName == "Named New Hire", "Placeholder receives actual name without duplicate profile");
            var leading = await db.BiometricEnrollments.Include(x => x.Employee).SingleAsync(x => x.EnrollCode == "008");
            Check(leading.Employee != null && leading.Employee.EmployeeCode != "008" && leading.Employee.FullName == "Leading Zero", "New enroll code creates named profile before first punch with separate ERP ID");
            Check((await db.BiometricEnrollments.FindAsync("22"))!.Status == "Excluded", "Deleted enrollment is not recreated");
            var count = await db.Employees.CountAsync();
            await BiometricEnrollmentService.SyncAsync(db, identities);
            Check(await db.Employees.CountAsync() == count, "Repeated directory synchronization is idempotent");
            var unassigned = Punch("5"); db.AttendanceLogs.AddRange(unassigned, Punch("008")); await db.SaveChangesAsync();
            await BiometricEmployeeReconciliationService.ReconcileAsync(db);
            Check((await db.AttendanceLogs.FindAsync(unassigned.Id))!.EmployeeId == null, "Conflicting punches remain unassigned for review");
            Check(await db.AttendanceLogs.AnyAsync(x => x.DeviceUserId == "008" && x.EmployeeId == leading.EmployeeId), "Punch resolves by enroll code including leading zeros");
            Check((await db.AttendanceLogs.FindAsync(historical.Id))!.EmployeeId == bob.Id, "Historical assigned attendance is preserved");
            var http = new DefaultHttpContext();
            var controller = new BiometricEnrollmentsController(db) { ControllerContext = new() { HttpContext = http }, TempData = new TempDataDictionary(http, new MemoryTempData()) };
            Check(await controller.Link("5", 0, false) is BadRequestResult, "Manual link requires explicit verification");
            Check(await controller.Link("5", 0, true) is RedirectToActionResult, "HR can resolve conflict to a new named profile");
            var resolved = await db.BiometricEnrollments.SingleAsync(x => x.EnrollCode == "5");
            Check((await db.AttendanceLogs.FindAsync(unassigned.Id))!.EmployeeId == resolved.EmployeeId && (await db.AttendanceLogs.FindAsync(historical.Id))!.EmployeeId == bob.Id, "Review links unassigned punches while preserving assigned history");

            // Exercise real importer with paginated WDMS-shaped payloads; no real WDMS writes.
            var services = new ServiceCollection(); services.AddDbContext<ApplicationDbContext>(x => x.UseNpgsql(connection.ConnectionString));
            using var provider = services.BuildServiceProvider();
            using var client = new HttpClient(new WdmsHandler());
            using var importer = new RemoteAttendanceImportService(provider.GetRequiredService<IServiceScopeFactory>(), new ClientFactory(client),
                Options.Create(new RemoteAttendanceOptions { BaseUrl = "https://wdms.invalid/", Username = "test", Password = "test" }), NullLogger<RemoteAttendanceImportService>.Instance);
            var import = typeof(RemoteAttendanceImportService).GetMethod("ImportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)import.Invoke(importer, new object[] { CancellationToken.None })!;
            db.ChangeTracker.Clear();
            Check(await db.BiometricEnrollments.AnyAsync(x => x.EnrollCode == "901" && x.FullName == "API Employee" && x.EmployeeId != null), "WDMS emp_code drives identity, not API row id");
            Check(await db.BiometricEnrollments.AnyAsync(x => x.EnrollCode == "902" && x.EmployeeId != null), "Directory pagination imports employees with no punches");
            Check(await db.AttendanceLogs.AnyAsync(x => x.DeviceUserId == "901" && x.EmployeeId != null), "End-to-end WDMS punch attaches to directory employee");
            var beforeReplay = await db.AttendanceLogs.CountAsync();
            await (Task)import.Invoke(importer, new object[] { CancellationToken.None })!;
            Check(await db.AttendanceLogs.CountAsync() == beforeReplay, "Repeated WDMS import does not duplicate punches");
            db.ChangeTracker.Clear();
            var completed = await db.Employees.SingleAsync(x => x.Id == leading.EmployeeId);
            completed.IsBiometricProfilePending = false;
            completed.FullName = "HR Corrected Name";
            completed.EmployeeCode = "HR-008";
            var disabled = await db.EmployeeDeviceMappings.SingleAsync(x => x.DeviceUserId == "008");
            disabled.IsActive = false;
            var disabledPunch = Punch("008");
            db.AttendanceLogs.Add(disabledPunch);
            await db.SaveChangesAsync();
            await BiometricEnrollmentService.SyncAsync(db, identities);
            await BiometricEmployeeReconciliationService.ReconcileAsync(db);
            Check(await db.Employees.AnyAsync(x => x.Id == leading.EmployeeId && x.FullName == "HR Corrected Name" && x.EmployeeCode == "HR-008"), "Directory refresh preserves completed HR profile edits");
            Check(await db.AttendanceLogs.AnyAsync(x => x.Id == disabledPunch.Id && x.EmployeeId == null)
                && await db.EmployeeDeviceMappings.AnyAsync(x => x.Id == disabled.Id && !x.IsActive), "Disabled device mapping stays disabled and its new punch stays unassigned");
            Console.WriteLine($"{checks} enrollment checks passed in an isolated database schema.");
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    sealed class WdmsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.Contains("login") && request.Method == HttpMethod.Get) body = "<input name='csrfmiddlewaretoken' value='test'>";
            else if (path.Contains("login")) { request.RequestUri = new Uri("https://wdms.invalid/iclock/api/"); body = "OK"; }
            else if (path.Contains("terminals")) body = """{"data":[{"sn":"ENROLL-TEST","alias":"Test"}],"next":null}""";
            else if (path.Contains("employees") && !request.RequestUri.Query.Contains("page=2")) body = """{"data":[{"id":"999999","emp_code":"901","first_name":"API","last_name":"Employee"}],"next":"/personnel/api/employees/?page=2"}""";
            else if (path.Contains("employees")) body = """{"data":[{"id":"888888","emp_code":"902","first_name":"No Punch","last_name":"Yet"}],"next":null}""";
            else body = """{"data":[{"id":123,"emp_code":"901","punch_time":"2026-10-09 09:00:00","punch_state":"0","verify_type":1,"terminal_sn":"ENROLL-TEST"}],"next":null}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
