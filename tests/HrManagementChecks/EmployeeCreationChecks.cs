using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Vertex_ERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class EmployeeCreationChecks
{
    public static async Task Run(string settingsPath, Action<bool, string> check)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "employee_check_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connection.ConnectionString, options => options.EnableRetryOnFailure()).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            var department = new Department { DepartmentCode = "TEST", DepartmentName = "Test" };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            var protection = new BankAccountProtectionService(new EphemeralDataProtectionProvider());
            HrController Controller()
            {
                var http = new DefaultHttpContext { Session = new MemorySession() };
                return new HrController(db, null!, NullLogger<HrController>.Instance, protection)
                {
                    ControllerContext = new ControllerContext { HttpContext = http },
                    TempData = new TempDataDictionary(http, new MemoryTempData())
                };
            }
            HrAddEmployeeViewModel Model(string code) => new()
            {
                CompanyCode = "VAS", EmployeeId = code, FirstName = "Test", LastName = "Employee",
                Email = code + "@example.invalid", Phone = code == "TEST1" ? "7000000001" : "7000000002",
                EmergencyContact = "7000000003", AadhaarNumber = "123456789012",
                DepartmentId = department.Id, Designation = "Tester",
                LoginUsername = code, TemporaryPassword = "TestPassword123!",
                BankAccountHolderName = "Test Employee", BankName = "Test Bank",
                BankAccountNumber = "123456789012", ConfirmBankAccountNumber = "123456789012",
                BankIfscCode = "TEST0123456", BasicSalary = 10000
            };
            var preview = (JsonResult)await Controller().EmployeeIdentityPreview("VAS", null);
            check(System.Text.Json.JsonSerializer.Serialize(preview.Value).Contains("VAS0180"), "Company selection previews next Automations ID");
            preview = (JsonResult)await Controller().EmployeeIdentityPreview("VPC", null);
            check(System.Text.Json.JsonSerializer.Serialize(preview.Value).Contains("VPC0178"), "Company selection previews next Power Controls ID");
            check((await db.EmployeeCompanies.AsNoTracking().SingleAsync(x => x.Code == "VAS")).LastIssuedNumber == 179,
                "Viewing ID preview does not consume a number");
            var controller = Controller();
            check(await controller.HrAddEmp(Model("TEST1")) is RedirectToActionResult { ControllerName: "Employee", ActionName: "Index" },
                "Employee creation succeeds with database retries enabled");
            db.ChangeTracker.Clear();
            var employee = await db.Employees.SingleAsync();
            check(employee.EmployeeCode == "VAS0180" && employee.CompanyCode == "VAS", "First Automations ID starts after VAS0179 and ignores supplied ID");
            var account = await db.AppUsers.SingleAsync();
            check(account.EmployeeId == employee.Id && PasswordHashService.VerifyPassword("TestPassword123!", account.PasswordHash),
                "Created employee login is linked and password works");
            check((await db.EmployeeSalaryDetails.SingleAsync()).EmployeeId == employee.Id &&
                protection.Unprotect((await db.EmployeeBankDetails.SingleAsync()).ProtectedAccountNumber) == "123456789012",
                "Employee bank and salary details saved together");

            // Force a later insert to fail and verify earlier inserts are rolled back.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"EmployeeSalaryDetails\" ADD CONSTRAINT reject_test_salary CHECK (\"BasicSalary\" < 20000)");
            var failing = Model("TEST2");
            failing.BasicSalary = 30000;
            check(await Controller().HrAddEmp(failing) is ViewResult, "Save failure returns the employee form");
            db.ChangeTracker.Clear();
            check(await db.Employees.CountAsync() == 1 && await db.AppUsers.CountAsync() == 1 &&
                await db.EmployeeBankDetails.CountAsync() == 1 && await db.EmployeeSalaryDetails.CountAsync() == 1,
                "Failed creation leaves no partial employee, login, bank or salary records");
            check((await db.EmployeeCompanies.SingleAsync(x => x.Code == "VAS")).LastIssuedNumber == 180,
                "Failed creation rolls back the company counter");
            await SalaryGenerationChecks.Run(db, check);
            await BiometricEmployeeChecks.Run(db, check);
            db.AttendanceLogs.Add(new AttendanceLog
            {
                BiometricDeviceId = await db.BiometricDevices.Select(x => x.Id).FirstAsync(),
                DeviceUserId = "ONBOARD176", PunchTime = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified),
                UniqueHash = Guid.NewGuid().ToString("N"), RawPayload = "TEST"
            });
            await db.SaveChangesAsync();
            await BiometricEmployeeReconciliationService.ReconcileAsync(db);
            var pending = await db.EmployeeDeviceMappings.Where(x => x.DeviceUserId == "ONBOARD176").Select(x => x.Employee).SingleAsync();
            var originalId = pending.Id;
            var originalCount = await db.Employees.CountAsync();
            var pendingForm = (HrAddEmployeeViewModel)((ViewResult)await Controller().HrAddEmp((int?)originalId)).Model!;
            check(pendingForm.PendingBiometricEmployeeId == originalId && pendingForm.EmployeeId == pending.EmployeeCode
                && pendingForm.SourceEnrollments.Any(x => x.StartsWith("ONBOARD176 ("))
                && string.IsNullOrEmpty(pendingForm.Email) && string.IsNullOrEmpty(pendingForm.FirstName),
                "Employee directory completion opens the same biometric ID without placeholder personal details");
            check(pendingForm.BiometricOptions.Any(x => x.EmployeeId == originalId && x.Label.Contains("ONBOARD176")),
                "Biometric selector includes source enrollment and employee identity");
            preview = (JsonResult)await Controller().EmployeeIdentityPreview("VAS", originalId);
            var identityJson = System.Text.Json.JsonSerializer.Serialize(preview.Value);
            check(identityJson.Contains("VAS0181") && identityJson.Contains("ONBOARD176"),
                "Identity preview displays both generated ERP ID and original biometric enrollment");
            check(await Controller().EmployeeIdentityPreview("VAS", int.MaxValue) is BadRequestObjectResult,
                "Unknown biometric profile cannot be previewed");
            check(await Controller().HrAddEmp((int?)int.MaxValue) is NotFoundResult, "Unknown completion profile returns not found");
            var lookup = (JsonResult)await Controller().LookupEmployeeForOnboarding(" onboard176 ");
            check(System.Text.Json.JsonSerializer.Serialize(lookup.Value).Contains("\"status\":\"pending\""), "Employee ID lookup recognizes pending biometric profile regardless of case");
            var completion = Model("ONBOARD176");

            completion.PendingBiometricEmployeeId = originalId;
            completion.EmployeeId = "ERP-176";
            completion.SourceEnrollments = new[] { "FAKE-SOURCE" };
            check(await Controller().HrAddEmp(completion) is RedirectToActionResult, "Add Employee completes biometric profile");
            db.ChangeTracker.Clear();
            var completed = await db.Employees.SingleAsync(x => x.Id == originalId);
            check(!completed.IsBiometricProfilePending && completed.EmployeeCode == "VAS0181" && completed.FullName == "Test Employee" && await db.Employees.CountAsync() == originalCount,
                "Completion updates the existing employee without creating a duplicate");
            check(await db.AttendanceLogs.AnyAsync(x => x.DeviceUserId == "ONBOARD176" && x.EmployeeId == originalId)
                && await db.EmployeeDeviceMappings.AnyAsync(x => x.DeviceUserId == "ONBOARD176" && x.EmployeeId == originalId),
                "Completion retains attendance and biometric mapping identity");
            check(await db.AppUsers.AnyAsync(x => x.EmployeeId == originalId) && await db.EmployeeBankDetails.AnyAsync(x => x.EmployeeId == originalId)
                && await db.EmployeeSalaryDetails.AnyAsync(x => x.EmployeeId == originalId), "Completion saves login, bank and salary against the same employee");
            lookup = (JsonResult)await Controller().LookupEmployeeForOnboarding("ONBOARD176");
            check(await Controller().HrAddEmp((int?)originalId) is RedirectToActionResult { ActionName: "Edit", ControllerName: "Employee" },
                "Completed profile link redirects to edit instead of onboarding again");
            check(System.Text.Json.JsonSerializer.Serialize(lookup.Value).Contains("\"status\":\"existing\""), "Completed employee lookup directs HR to Edit Profile");
            var power = Model("POWER1");
            power.CompanyCode = "VPC"; power.Phone = "7000000004";
            check(await Controller().HrAddEmp(power) is RedirectToActionResult, "Power Controls employee saves successfully");
            db.ChangeTracker.Clear();
            check(await db.Employees.AnyAsync(x => x.EmployeeCode == "VPC0178" && x.CompanyCode == "VPC"), "Power Controls starts at VPC0178 independently");
            var invalid = Model("INVALID"); invalid.CompanyCode = "OTHER";
            check(await Controller().HrAddEmp(invalid) is ViewResult, "Unknown company is rejected by server");
            completion.FirstName = "Overwrite attempt";
            check(await Controller().HrAddEmp(completion) is ViewResult && (await db.Employees.AsNoTracking().SingleAsync(x => x.Id == originalId)).FirstName == "Test",
                "Repeated completion cannot overwrite finished employee");
            await EmployeeNumberingChecks.Run(options, check);
        }
        finally
        {
            // Only the isolated schema created by this test run is removed.
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }

    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    sealed class MemorySession : ISession
    {
        readonly Dictionary<string, byte[]> data = new();
        public bool IsAvailable => true;
        public string Id => "employee-test";
        public IEnumerable<string> Keys => data.Keys;
        public void Clear() => data.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => data.Remove(key);
        public void Set(string key, byte[] value) => data[key] = value;
        public bool TryGetValue(string key, out byte[] value) => data.TryGetValue(key, out value!);
    }
}
