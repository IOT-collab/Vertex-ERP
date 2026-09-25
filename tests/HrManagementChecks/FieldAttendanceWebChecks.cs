using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class FieldAttendanceWebChecks
{
    public static async Task Run(string settingsPath)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "field_web_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        Process? process = null;
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        try
        {
            connection.SearchPath = schema;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            // Reconstruct the prior schema, so real application startup must apply both pending migrations.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AttendanceLogs\" DROP COLUMN \"FieldSiteName\", DROP COLUMN \"LocationCapturedAtUtc\"; ALTER TABLE \"Employees\" DROP COLUMN \"IsBiometricProfilePending\"; CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL)");
            foreach (var migration in db.Database.GetMigrations().Where(id => !id.EndsWith("_AddBiometricProfilePending") && !id.EndsWith("_AddFieldAttendanceSiteDetails")))
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" VALUES ({migration}, {"8.0.8"})");

            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var origin = new Uri($"http://127.0.0.1:{port}");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(root, "bin", "Release", "net8.0", "Vertex ERP.dll"));
            start.Environment["ConnectionStrings__DefaultConnection"] = connection.ConnectionString;
            start.Environment["ASPNETCORE_URLS"] = origin.ToString();
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["RemoteAttendance__Enabled"] = "false";
            start.Environment["SeedUsers__Password"] = Guid.NewGuid().ToString("N");
            start.Environment["Logging__LogLevel__Default"] = "Warning";
            process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) logs.Enqueue(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) logs.Enqueue(e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(20) };
            using var anonymous = Client();
            bool ready = false;
            for (int i = 0; i < 60 && !process.HasExited; i++)
            {
                try { ready = (await anonymous.GetAsync("/Main/Login")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { }
                await Task.Delay(500);
            }
            Check(ready, "Actual app starts and applies pending migrations");
            Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "No database migrations remain pending");
            var password = "Test-" + Guid.NewGuid().ToString("N");
            Employee Employee(string code) => new() { EmployeeCode = code, FirstName = code, FullName = code, Email = code + "@example.invalid", PhoneNumber = code, Department = "Web Test", Designation = "Tester" };
            var manager = Employee("WEB_MANAGER"); var employee = Employee("WEB_EMPLOYEE"); var outsider = Employee("WEB_OUTSIDER");
            employee.ReportingManager = manager;
            db.Employees.AddRange(manager, employee, outsider); await db.SaveChangesAsync();
            foreach (var (name, role, person) in new[] { ("WEB_EMPLOYEE", "Employee", employee), ("WEB_MANAGER", "Manager", manager), ("WEB_HR", "HR", (Employee?)null), ("WEB_ADMIN", "Admin", (Employee?)null) })
                db.AppUsers.Add(new AppUser { Username = name, NormalizedUsername = name, FullName = name, Role = role, EmployeeId = person?.Id, PasswordHash = PasswordHashService.HashPassword(password), MustChangePassword = false });
            await db.SaveChangesAsync();
            string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            async Task Login(HttpClient client, string name)
            {
                var page = await client.GetStringAsync("/Main/Login");
                var response = await client.PostAsync("/Main/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = name, ["password"] = password, ["__RequestVerificationToken"] = Token(page) }));
                Check(response.StatusCode == HttpStatusCode.Redirect, name + " authenticates through real login form");
            }
            Check((await anonymous.GetAsync("/Main/FieldAttendance")).StatusCode == HttpStatusCode.Redirect, "Anonymous attendance access requires login");
            using var employeeClient = Client(); await Login(employeeClient, "WEB_EMPLOYEE");
            var fieldPage = await employeeClient.GetStringAsync("/Main/FieldAttendance");
            Check(fieldPage.Contains("id=\"siteName\"") && fieldPage.Contains("Check In"), "Employee field-attendance Razor page renders");
            employeeClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Token(fieldPage));
            object Punch(string action, string site, decimal latitude = 28.6139m) => new { action, siteName = site, latitude, longitude = 77.2090m, accuracyMetres = 10, capturedAtUtc = DateTimeOffset.UtcNow };
            Check((await employeeClient.PostAsJsonAsync("/Main/SubmitFieldAttendance", Punch("Check In", "Site Alpha", 100))).StatusCode == HttpStatusCode.BadRequest, "HTTP validation rejects invalid coordinates");
            Check((await employeeClient.PostAsJsonAsync("/Main/SubmitFieldAttendance", Punch("Check In", "Site Alpha"))).IsSuccessStatusCode, "Employee submits check-in through authenticated HTTP endpoint");
            Check((await employeeClient.PostAsJsonAsync("/Main/SubmitFieldAttendance", Punch("Check In", "Site Alpha"))).StatusCode == HttpStatusCode.Conflict, "HTTP duplicate check-in rejected");
            Check((await employeeClient.PostAsJsonAsync("/Main/SubmitFieldAttendance", Punch("Check Out", "Site Beta", 28.62m))).IsSuccessStatusCode, "Employee submits check-out at another site");
            fieldPage = await employeeClient.GetStringAsync("/Main/FieldAttendance");
            Check(fieldPage.Contains("Site Alpha") && fieldPage.Contains("Site Beta"), "Employee sees saved records after reload");
            var denied = await employeeClient.GetAsync("/Main/LocationTracking");
            Check(denied.StatusCode == HttpStatusCode.Forbidden || (denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location!.ToString().Contains("AccessDenied")), "Employee cannot open HR/team tracking");
            var device = new BiometricDevice { SerialNumber = "WEB-BIO", Name = "Test biometric", Model = "Test" };
            db.BiometricDevices.Add(device);
            db.AttendanceLogs.Add(new AttendanceLog { BiometricDevice = device, DeviceUserId = "NEWBIO901", PunchTime = FieldAttendanceClock.Now.Date.AddHours(9).AddMinutes(30), RawPayload = "TEST", UniqueHash = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            await BiometricEmployeeReconciliationService.ReconcileAsync(db);
            var imported = await db.Employees.SingleAsync(e => e.EmployeeCode == "NEWBIO901");
            foreach (var name in new[] { "WEB_MANAGER", "WEB_HR", "WEB_ADMIN" })
            {
                using var viewer = Client(); await Login(viewer, name);
                var tracking = await viewer.GetStringAsync($"/Main/LocationTracking?employeeId={employee.Id}");
                Check(tracking.Contains("Site Alpha") && tracking.Contains("Site Beta") && tracking.Contains("28.613900,77.209000"), name + " sees saved sites and GPS map link");
                Check(tracking.Contains($"value=\"{DateOnly.FromDateTime(FieldAttendanceClock.Now):yyyy-MM-dd}\""), name + " sees default date populated");
                if (name == "WEB_MANAGER")
                {
                    var other = await viewer.GetStringAsync($"/Main/LocationTracking?employeeId={outsider.Id}");
                    Check(!other.Contains("WEB_OUTSIDER"), "Manager cannot reveal outsiders using a changed URL");
                }
                else
                {
                    var directory = await viewer.GetStringAsync("/Employee/Index?status=pending");
                    Check(directory.Contains("NEWBIO901") && directory.Contains("Complete Profile"), name + " sees new biometric employee and completion link");
                    var onboarding = await viewer.GetStringAsync($"/Hr/HrAddEmp?biometricEmployeeId={imported.Id}");
                    Check(onboarding.Contains("value=\"NEWBIO901\"") && onboarding.Contains("Complete Employee Profile"), name + " opens prefilled full profile form");
                    var attendance = await viewer.GetStringAsync($"/Main/Attendence?filterDate={DateOnly.FromDateTime(FieldAttendanceClock.Now):yyyy-MM-dd}");
                    Check(attendance.Contains("NEWBIO901"), name + " sees imported employee in attendance");
                }
            }
            using var hr = Client(); await Login(hr, "WEB_HR");
            var form = await hr.GetStringAsync($"/Hr/HrAddEmp?biometricEmployeeId={imported.Id}");
            var department = new Department { DepartmentCode = "WEB", DepartmentName = "Web Test" };
            db.Departments.Add(department); await db.SaveChangesAsync();
            var employeeCount = await db.Employees.CountAsync();
            var completedResponse = await hr.PostAsync("/Hr/HrAddEmp", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token(form), ["PendingBiometricEmployeeId"] = imported.Id.ToString(),
                ["EmployeeId"] = "NEWBIO901", ["FirstName"] = "Completed", ["LastName"] = "Biometric",
                ["Email"] = "completed@example.invalid", ["Phone"] = "7000000901", ["EmergencyContact"] = "7000000902",
                ["AadhaarNumber"] = "123456789012", ["DepartmentId"] = department.Id.ToString(), ["Designation"] = "Engineer",
                ["Position"] = "Employee", ["ReportingManagerId"] = manager.Id.ToString(), ["JoiningDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["LoginUsername"] = "completed.bio", ["TemporaryPassword"] = password,
                ["BankAccountHolderName"] = "Completed Biometric", ["BankName"] = "Test Bank", ["BankAccountNumber"] = "123456789012",
                ["ConfirmBankAccountNumber"] = "123456789012", ["BankIfscCode"] = "TEST0123456", ["BasicSalary"] = "10000"
            }));
            Check(completedResponse.StatusCode == HttpStatusCode.Redirect, "HR submits full biometric employee details through real form");
            db.ChangeTracker.Clear();
            var completed = await db.Employees.SingleAsync(e => e.Id == imported.Id);
            Check(!completed.IsBiometricProfilePending && completed.FullName == "Completed Biometric" && await db.Employees.CountAsync() == employeeCount,
                "Full form completes the same employee without duplication");
            Check(await db.AttendanceLogs.AnyAsync(l => l.EmployeeId == imported.Id && l.DeviceUserId == "NEWBIO901")
                && await db.EmployeeBankDetails.AnyAsync(b => b.EmployeeId == imported.Id) && await db.EmployeeSalaryDetails.AnyAsync(s => s.EmployeeId == imported.Id),
                "Attendance, bank and salary remain linked after HTTP completion");
            using var completedClient = Client(); await Login(completedClient, "completed.bio");
            var payMonth = DateTime.Today.AddMonths(-1);
            db.EmployeeSalaryDetails.Add(new EmployeeSalaryDetail { EmployeeId = employee.Id, BasicSalary = 12000, IsActive = true });
            await db.SaveChangesAsync();
            var salaryPage = await hr.GetStringAsync("/Hr/SalarySlips");
            Check(salaryPage.Contains("Generate this employee"), "Salary page offers individual generation buttons");
            var generate = await hr.PostAsync("/Hr/GenerateSalarySlips", new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["__RequestVerificationToken"] = Token(salaryPage), ["year"] = payMonth.Year.ToString(), ["month"] = payMonth.Month.ToString(),
                ["singleEmployeeId"] = imported.Id.ToString(), ["employeeIds"] = employee.Id.ToString(),
                [$"salaryDays_{imported.Id}"] = "20", [$"leaveDeduction_{imported.Id}"] = "0"
            }));
            Check(generate.StatusCode == HttpStatusCode.Redirect && await db.GeneratedSalarySlips.CountAsync() == 1
                && await db.GeneratedSalarySlips.AnyAsync(s => s.EmployeeId == imported.Id), "Individual HTTP generation ignores other selected employees");
            var generated = await db.GeneratedSalarySlips.AsNoTracking().SingleAsync();
            var revisePage = await hr.GetStringAsync($"/SalarySlipRevision/Edit/{generated.Id}");
            Check(revisePage.Contains("Correction reason") && revisePage.Contains("Salary Days"), "Revision page renders editable salary fields");
            var formValues = new Dictionary<string,string> { ["__RequestVerificationToken"] = Token(revisePage), ["Id"] = generated.Id.ToString(),
                ["Version"] = generated.UpdatedAtUtc.Ticks.ToString(), ["Reason"] = "Correct month days" };
            foreach (var item in SalarySlipRevisionViewModel.Snapshot(generated)) formValues["Values[" + item.Key + "]"] = item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            formValues["Values[SalaryDays]"] = DateTime.DaysInMonth(payMonth.Year,payMonth.Month).ToString();
            var revised = await hr.PostAsync("/SalarySlipRevision/Edit",new FormUrlEncodedContent(formValues));
            Check(revised.StatusCode == HttpStatusCode.Redirect, "HR revises generated slip through actual form");
            var historyPage = await hr.GetStringAsync($"/SalarySlipRevision/Edit/{generated.Id}");
            Check(historyPage.Contains("Correct month days") && historyPage.Contains("Before") && historyPage.Contains("After"), "Revision history renders previous and corrected values");
            var blockedRevision = await employeeClient.GetAsync($"/SalarySlipRevision/Edit/{generated.Id}");
            Check(blockedRevision.StatusCode == HttpStatusCode.Forbidden || (blockedRevision.StatusCode == HttpStatusCode.Redirect && blockedRevision.Headers.Location!.ToString().Contains("AccessDenied")), "Employees cannot revise salary slips");
            var salaryPdf = await completedClient.GetAsync($"/Main/DownloadSalarySlip?year={payMonth.Year}&month={payMonth.Month}");
            Check(salaryPdf.IsSuccessStatusCode && salaryPdf.Content.Headers.ContentType?.MediaType == "application/pdf", "Employee downloads corrected salary PDF");
            Console.WriteLine($"{passed} real HTTP/startup checks passed (test GPS coordinates).");
        }
        catch
        {
            foreach (var line in logs.TakeLast(20)) Console.WriteLine(line);
            throw;
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose();
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }
}
