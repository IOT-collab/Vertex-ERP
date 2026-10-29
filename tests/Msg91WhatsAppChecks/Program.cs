using System.Net;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using VertexERP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
Msg91WhatsAppOptions Config() => new() { SendingEnabled = true, TemplateMappingVerified = true, AuthKey = "TEST-KEY-NOT-REAL",
    ApprovedTemplateText = "Test fixture only: Employee {{1}}, task {{2}}, assigned by {{3}}, deadline {{4}}.",
    ParameterOrder = ["EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline"] };
var data = new TaskWhatsAppData("Test Employee", "+91 98765 43210", "Review & approve \"report\"", "Test Manager", new DateOnly(2026, 11, 2));
var config = Config();
Check(config.Ready && config.ReadinessIssues.Count == 0, "Complete confirmed provider configuration is ready when sending is explicitly enabled");
foreach (var (disable, expectedIssue) in new (Action<Msg91WhatsAppOptions> Disable, string Issue)[] {
    (x => x.SendingEnabled = false, "sending_disabled"),
    (x => x.AuthKey = "", "auth_key_missing_or_invalid"),
    (x => x.ApiUrl = "http://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/", "api_url_invalid"),
    (x => x.ApiUrl = "https://api.msg91.com:444/api/v5/whatsapp/whatsapp-outbound-message/bulk/", "api_url_invalid"),
    (x => x.IntegratedNumber = "123", "integrated_number_invalid"),
    (x => x.TemplateName = "", "template_name_missing_or_invalid"),
    (x => x.LanguageCode = "", "language_code_missing_or_invalid"),
    (x => x.Namespace = "", "namespace_missing_or_invalid") })
{
    var incomplete = Config(); disable(incomplete);
    Check(!incomplete.Ready && incomplete.ReadinessIssues.Contains(expectedIssue), "Readiness diagnostics identify " + expectedIssue);
}
Check(new Msg91WhatsAppOptions().ReadinessIssues.Contains("sending_disabled"), "Live sending remains disabled by default");
Check(DatabaseInitializer.ShouldInitializeAtStartup("Development"), "Development environment retains startup migrations and seed initialization");
Check(!DatabaseInitializer.ShouldInitializeAtStartup("Production")
    && !DatabaseInitializer.ShouldInitializeAtStartup("Staging"), "Production and staging skip startup migrations and development-user seeding");
Check(Msg91AuthKeySecretMapping.IsTargetSecret("Msg91-AuthKey")
    && Msg91AuthKeySecretMapping.MapSecretNameToConfigurationKey("Msg91-AuthKey") == "Msg91WhatsApp:AuthKey",
    "Existing Key Vault secret maps to the bound MSG91 AuthKey setting");
Check(!Msg91AuthKeySecretMapping.IsTargetSecret("Unrelated-Secret"),
    "Key Vault secret mapping ignores unrelated secret names");
var configSearch = new DirectoryInfo(AppContext.BaseDirectory);
while (configSearch is not null && !File.Exists(Path.Combine(configSearch.FullName, "Vertex ERP", "appsettings.json"))) configSearch = configSearch.Parent;
Check(configSearch is not null, "Repository appsettings.json is available for configuration binding check");
if (configSearch is not null)
{
    var rootConfiguration = new ConfigurationBuilder().AddJsonFile(Path.Combine(configSearch.FullName, "Vertex ERP", "appsettings.json")).Build();
    var appSettingsOptions = new Msg91WhatsAppOptions();
    rootConfiguration.GetSection(Msg91WhatsAppOptions.Section).Bind(appSettingsOptions);
    Check(!appSettingsOptions.SendingEnabled && appSettingsOptions.AuthKey == "", "Committed appsettings keep sending disabled and AuthKey empty");
    Check(appSettingsOptions.ApiUrl == Msg91WhatsAppService.Endpoint && appSettingsOptions.IntegratedNumber == "919217614338"
        && appSettingsOptions.TemplateName == "vertex_task_assigned" && appSettingsOptions.LanguageCode == "en"
        && appSettingsOptions.Namespace == "cdc730df_d16e_48b9_94b9_a057d00e67a1", "Committed appsettings bind the confirmed provider settings");
    Check(appSettingsOptions.ParameterOrder.SequenceEqual(new[] { "EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline" }), "Configured parameter order binds exactly once");
    Check(string.Join(',', appSettingsOptions.ReadinessIssues.Order()) == "auth_key_missing_or_invalid,sending_disabled",
        "Committed appsettings readiness reports only missing secret and disabled sending");
}
var handler = new StubHandler();
var sender = new Msg91WhatsAppService(new HttpClient(handler), Options.Create(config));
var result = await sender.SendTaskAsync(data, default);
Check(result.Outcome == "Accepted" && result.ProviderId == "bulk-test-123" && result.HttpStatus == 200, "Bulk request acceptance and provider request ID recorded");
Check(result.SubmittedAtUtc.HasValue && result.SanitizedResponse == "{\"status\":\"success\",\"hasError\":false,\"provider_id\":\"bulk-test-123\"}", "Submission timestamp and allow-listed provider response captured");
Check(!result.SanitizedResponse!.Contains("919876543210") && !result.SanitizedResponse.Contains("secret-echo") && !result.SanitizedResponse.Contains("Private"), "Sanitized provider response excludes phone, credentials, and personal data");
Check(handler.Method == HttpMethod.Post && handler.Url == Msg91WhatsAppService.Endpoint, "Exact confirmed POST endpoint, including trailing slash");
Check(handler.Auth == config.AuthKey && handler.ContentType == "application/json", "Authkey header and JSON content type");
var expected = JsonNode("""
{
  "integrated_number":"919217614338", "content_type":"template",
  "payload": { "messaging_product":"whatsapp", "type":"template", "template": {
    "name":"vertex_task_assigned", "language":{"code":"en","policy":"deterministic"},
    "namespace":"cdc730df_d16e_48b9_94b9_a057d00e67a1", "to_and_components":[{
      "to":["919876543210"], "components":{
        "body_1":{"type":"text","value":"Test Employee"},
        "body_2":{"type":"text","value":"Review & approve \"report\""},
        "body_3":{"type":"text","value":"Test Manager"},
        "body_4":{"type":"text","value":"02 Nov 2026"}
      }
    }]
  }}
}
""");
Check(System.Text.Json.Nodes.JsonNode.DeepEquals(expected, JsonNode(handler.Body!)), "Exact user-provided nested JSON structure; no extra API fields");
Check(!handler.Body!.Contains(config.AuthKey), "Credentials never enter request JSON");
config.ParameterOrder = ["TaskTitle", "EmployeeName", "TaskDeadline", "AssignedBy"];
var noCallForWrongOrder = handler.Calls;
Check((await sender.SendTaskAsync(data, default)).Outcome == "Disabled" && handler.Calls == noCallForWrongOrder,
    "Unapproved template parameter order blocks provider requests");
config.ParameterOrder = ["EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline"];
foreach (var (phone, expectedPhone) in new[] { ("9876543210", "919876543210"), ("09876543210", "919876543210"), ("00919876543210", "919876543210"), ("+44 7700 900123", "447700900123") })
    Check(Msg91WhatsAppService.NormalizePhone(phone) == expectedPhone, "Phone normalization: " + phone);
foreach (var bad in new[] { "", "BIO-123", "9876543210oops", "123", "+1234567890123456" })
    Check(Msg91WhatsAppService.NormalizePhone(bad) == null, "Invalid phone rejected: " + bad);
var calls = handler.Calls;
Check((await sender.SendTaskAsync(data with { Phone = "not-a-number" }, default)).Outcome == "Failed" && handler.Calls == calls, "Invalid phone never calls provider");
foreach (var disable in new Action<Msg91WhatsAppOptions>[] { x => x.SendingEnabled = false, x => x.TemplateMappingVerified = false, x => x.AuthKey = "", x => x.ApprovedTemplateText = "", x => x.ParameterOrder = ["EmployeeName","EmployeeName","AssignedBy","TaskDeadline"], x => x.ApprovedTemplateText = "Only {{1}}" })
{
    var disabled = Config(); disable(disabled);
    var gated = new Msg91WhatsAppService(new HttpClient(handler), Options.Create(disabled)); calls = handler.Calls;
    Check((await gated.SendTaskAsync(data, default)).Outcome == "Disabled" && handler.Calls == calls, "Unapproved or incomplete configuration cannot send");
}
foreach (var (status, body, outcome) in new[] {
    (400, "{\"status\":\"error\"}", "Failed"), (401, "{\"hasError\":true}", "Failed"),
    (429, "{}", "Retry"), (503, "{\"hasError\":true}", "Retry"),
    (503, "upstream unavailable", "NeedsReview"), (200, "not json", "NeedsReview"),
    (200, "{\"status\":\"success\"}", "NeedsReview"), (200, "{\"hasError\":true}", "Failed"),
    (302, "", "Failed") })
{
    handler.Status = status; handler.ResponseBody = body;
    result = await sender.SendTaskAsync(data, default);
    Check(result.Outcome == outcome && result.HttpStatus == status, $"HTTP {status} {outcome} classification");
}
handler.Status = 429; handler.RetryAfter = TimeSpan.FromMinutes(3);
Check((await sender.SendTaskAsync(data, default)).RetryAfter == TimeSpan.FromMinutes(3), "Retry-After honored");
handler.Exception = new TaskCanceledException();
Check((await sender.SendTaskAsync(data, default)).Outcome == "NeedsReview", "Timeout does not blindly duplicate a possibly accepted request");
handler.Exception = new HttpRequestException(HttpRequestError.NameResolutionError, "TEST");
Check((await sender.SendTaskAsync(data, default)).Outcome == "Retry", "DNS failure safely retries");
handler.Exception = new HttpRequestException("TEST");
Check((await sender.SendTaskAsync(data, default)).Outcome == "NeedsReview", "Ambiguous network failure held for review");
handler.Exception = null; handler.Status = 200; handler.ResponseBody = "{\"status\":\"success\",\"hasError\":false,\"request_id\":\"bulk-test-123\"}";
var queue = new FakeQueue(data);
var dispatcher = new WhatsAppTaskDispatcher(queue, sender, Options.Create(config), NullLogger<WhatsAppTaskDispatcher>.Instance);
calls = handler.Calls;
Check(await dispatcher.ProcessNextAsync(default), "Dispatcher processes saved queue item");
Check(!await dispatcher.ProcessNextAsync(default) && handler.Calls == calls + 1, "Accepted queue item cannot be claimed twice");
Check(queue.Item.Status == "Accepted" && queue.Item.ProviderMessageId == "bulk-test-123", "Queue stores provider acceptance result");
var skipped = new FakeQueue(null);
Check(await new WhatsAppTaskDispatcher(skipped, sender, Options.Create(config), NullLogger<WhatsAppTaskDispatcher>.Instance).ProcessNextAsync(default) && skipped.Item.Status == "Skipped" && handler.Calls == calls + 1, "Deleted/reassigned/inactive recipient skips sending");
var gatedQueue = new FakeQueue(data); var off = new Msg91WhatsAppOptions();
Check(!await new WhatsAppTaskDispatcher(gatedQueue, sender, Options.Create(off), NullLogger<WhatsAppTaskDispatcher>.Instance).ProcessNextAsync(default) && gatedQueue.Claims == 0, "Disabled worker leaves queued work untouched");
var retryItem = new WhatsAppTaskNotification { Attempts = 1 };
WhatsAppTaskQueue.ApplyResult(retryItem, new("Retry", 429, RetryAfter: TimeSpan.FromMinutes(3)));
Check(retryItem.Status == "Retry" && retryItem.NextAttemptAtUtc > DateTime.UtcNow.AddSeconds(170), "Durable retry schedule with provider backoff");
retryItem.Attempts = 5; WhatsAppTaskQueue.ApplyResult(retryItem, new("Retry", 503));
Check(retryItem.Status == "Failed" && retryItem.ErrorCode == "retry_limit_reached", "Retry limit stops endless retries");

// Exercise the actual central SaveChanges hook without opening a database connection.
var suppress = new SaveProbe();
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused;Database=unused;Username=unused").AddInterceptors(suppress).Options);
var task = new WorkTask { Title = "New task", ManagerId = 10, AssigneeId = 20, DueDate = new(2026,11,2) };
db.WorkTasks.Add(task);
await db.SaveChangesAsync();
Check(db.ChangeTracker.Entries<WhatsAppTaskNotification>().Count() == 1, "Any new WorkTask stages a durable notification in its own save");
var staged = db.ChangeTracker.Entries<WhatsAppTaskNotification>().Single().Entity;
Check(ReferenceEquals(staged.Task, task) && staged.AssigneeId == 20, "Queue uses task relationship and actual assignee, not client WhatsApp input");
await db.SaveChangesAsync();
Check(db.ChangeTracker.Entries<WhatsAppTaskNotification>().Count() == 1, "Repeating SaveChanges before commit cannot stage duplicate notifications");
var model = db.Model.FindEntityType(typeof(WhatsAppTaskNotification))!;
Check(model.GetIndexes().Any(i => i.IsUnique && i.Properties.Single().Name == "TaskId"), "Database enforces one notification per task");
Check(model.GetForeignKeys().Single().DeleteBehavior == DeleteBehavior.Cascade, "Unsent notifications cannot survive task deletion");
db.ChangeTracker.Clear();
var existing = new WorkTask { Id = 123, Title = "Existing", ManagerId = 10, AssigneeId = 20, DueDate = new(2026,11,2) }; db.Attach(existing); existing.Title = "Edited";
await db.SaveChangesAsync();
Check(!db.ChangeTracker.Entries<WhatsAppTaskNotification>().Any(), "Existing task edits do not resend new-assignment notification");
db.ChangeTracker.Clear(); db.WorkTasks.Add(new WorkTask { Title = "Failure", ManagerId = 10, AssigneeId = 20 }); suppress.Fail = true;
try { await db.SaveChangesAsync(); throw new Exception("Save should fail"); } catch (TestSaveException) { }
Check(handler.Calls == calls + 1 && db.ChangeTracker.Entries<WhatsAppTaskNotification>().All(e => e.State == EntityState.Added), "Failed task save makes no HTTP request and cannot publish a committed notification");
var migrationSql = db.GetService<IMigrator>().GenerateScript("20261028113951_RequireManagerFirstLeaveFlow", "20261029070359_AddWhatsAppTaskNotificationQueue");
Check(migrationSql.Contains("CREATE TABLE \"WhatsAppTaskNotifications\"") && migrationSql.Contains("CREATE UNIQUE INDEX \"IX_WhatsAppTaskNotifications_TaskId\""), "PostgreSQL migration generates queue table and unique deduplication index offline");
Check(!migrationSql.Contains("ALTER TABLE \"Tasks\"") && !migrationSql.Contains("UPDATE \"Employees\""), "Migration leaves existing tasks and employee data unchanged");
Check(Msg91WhatsAppService.NormalizePhone(null) == null, "Legacy null phone fails validation safely");
await DeliveryChecks.Run(Check);
var deliveryMigration = db.GetService<IMigrator>().GenerateScript("20261029070359_AddWhatsAppTaskNotificationQueue", "AddMsg91DeliveryTracking");
Check(deliveryMigration.Contains("CREATE TABLE \"WhatsAppDeliveryEvents\"") && deliveryMigration.Contains("RecipientNumberHash") && deliveryMigration.Contains("CREATE UNIQUE INDEX"), "Delivery migration generates authenticated receipt inbox and status columns offline");
var integrationConnection = Environment.GetEnvironmentVariable("VERTEXERP_INTEGRATION_TEST_CONNECTION");
if (!string.IsNullOrWhiteSpace(integrationConnection))
{
    var parsedConnection = new Npgsql.NpgsqlConnectionStringBuilder(integrationConnection);
    var safeLoopbackDatabase = parsedConnection.Host is "127.0.0.1" or "localhost" or "::1"
        && parsedConnection.Port == 55439 && parsedConnection.Database == "vertexerp_test";
    if (!safeLoopbackDatabase)
        throw new InvalidOperationException("Integration test refuses to connect unless using loopback port 55439 and database vertexerp_test.");
    Check(safeLoopbackDatabase,
        "Database integration test is constrained to an explicitly named loopback test database");
    var integrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(integrationConnection).Options;
    await using var integrationDb = new ApplicationDbContext(integrationOptions);
    Check(await integrationDb.Database.CanConnectAsync(), "Isolated PostgreSQL development database is reachable");

    var manager = new Employee
    {
        EmployeeCode = "IT-MGR-" + Guid.NewGuid().ToString("N")[..8], FirstName = "Integration", LastName = "Manager",
        FullName = "Integration Manager", Email = $"manager-{Guid.NewGuid():N}@example.test", PhoneNumber = "+919111111111",
        JoiningDate = DateOnly.FromDateTime(DateTime.Today), Department = "Integration Test", Designation = "Manager"
    };
    var assignee = new Employee
    {
        EmployeeCode = "IT-EMP-" + Guid.NewGuid().ToString("N")[..8], FirstName = "Integration", LastName = "Employee",
        FullName = "Integration Employee", Email = $"employee-{Guid.NewGuid():N}@example.test", PhoneNumber = "+919222222222",
        JoiningDate = DateOnly.FromDateTime(DateTime.Today), Department = "Integration Test", Designation = "Employee"
    };
    integrationDb.Employees.AddRange(manager, assignee);
    await integrationDb.SaveChangesAsync();
    var managerUser = new AppUser
    {
        Username = "it-manager-" + Guid.NewGuid().ToString("N")[..8],
        NormalizedUsername = Guid.NewGuid().ToString("N").ToUpperInvariant(), PasswordHash = "test-only",
        Role = "Manager", FullName = manager.FullName, EmployeeId = manager.Id
    };
    integrationDb.AppUsers.Add(managerUser);
    await integrationDb.SaveChangesAsync();

    var controller = new TaskManagementController(integrationDb)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, managerUser.Id.ToString()),
                    new Claim(ClaimTypes.Role, "Manager")
                }, "IntegrationTest"))
            }
        }
    };
    var create = await controller.CreateTask(new CreateWorkTaskRequest
    {
        Title = "Isolated queue integration test", ManagerId = manager.Id, AssigneeId = assignee.Id,
        Priority = "Medium", DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3))
    }, default);
    Check(create is CreatedAtActionResult, "Actual task assignment controller accepts a valid manager-to-team task");
    var createdTaskId = await integrationDb.WorkTasks.AsNoTracking()
        .Where(task => task.ManagerId == manager.Id && task.AssigneeId == assignee.Id && task.Title == "Isolated queue integration test")
        .Select(task => task.Id).SingleAsync();
    var taskQueueEntries = await integrationDb.WhatsAppTaskNotifications.AsNoTracking()
        .Where(notification => notification.TaskId == createdTaskId).ToListAsync();
    Check(taskQueueEntries.Count == 1 && taskQueueEntries[0].AssigneeId == assignee.Id && taskQueueEntries[0].Status == "Pending",
        "Persisted task assignment creates exactly one pending WhatsApp queue row for its assignee");

    var disabledHandler = new StubHandler();
    var disabledSender = new Msg91WhatsAppService(new HttpClient(disabledHandler), Options.Create(new Msg91WhatsAppOptions()));
    var disabledDispatcher = new WhatsAppTaskDispatcher(new WhatsAppTaskQueue(integrationDb), disabledSender,
        Options.Create(new Msg91WhatsAppOptions()), NullLogger<WhatsAppTaskDispatcher>.Instance);
    Check(!await disabledDispatcher.ProcessNextAsync(default) && disabledHandler.Calls == 0,
        "SendingEnabled=false prevents every outbound MSG91 HTTP call during real queue processing");
    var unchangedQueue = await integrationDb.WhatsAppTaskNotifications.AsNoTracking().SingleAsync(notification => notification.TaskId == createdTaskId);
    Check(unchangedQueue.Status == "Pending" && unchangedQueue.Attempts == 0,
        "Disabled sending preserves the pending test notification without claiming or sending it");
}
else
{
    Console.WriteLine("SKIP: PostgreSQL integration checks (VERTEXERP_INTEGRATION_TEST_CONNECTION is unset).");
}
Console.WriteLine($"PASS: {count} MSG91 checks; mocked HTTP only; no live messages sent.");
static System.Text.Json.Nodes.JsonNode? JsonNode(string json) => System.Text.Json.Nodes.JsonNode.Parse(json);

sealed class StubHandler : HttpMessageHandler
{
    public int Calls, Status = 200; public HttpMethod? Method; public string? Url, Auth, Body, ContentType;
    public string ResponseBody = "{\"status\":\"success\",\"hasError\":false,\"request_id\":\"bulk-test-123\",\"customerNumber\":\"919876543210\",\"authkey\":\"secret-echo\",\"employeeName\":\"Private\"}";
    public Exception? Exception; public TimeSpan? RetryAfter;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++; Method = request.Method; Url = request.RequestUri!.ToString(); Auth = request.Headers.GetValues("authkey").Single();
        Body = await request.Content!.ReadAsStringAsync(token); ContentType = request.Content.Headers.ContentType!.MediaType;
        if (Exception != null) throw Exception;
        var response = new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(ResponseBody) };
        if (RetryAfter.HasValue) response.Headers.RetryAfter = new(RetryAfter.Value);
        return response;
    }
}
sealed class FakeQueue(TaskWhatsAppData? data) : IWhatsAppTaskQueue
{
    public WhatsAppTaskNotification Item = new() { Id = 1, TaskId = 123, AssigneeId = 20 }; public int Claims;
    public Task<WhatsAppTaskNotification?> ClaimAsync(CancellationToken token)
    { Claims++; if (Item.Status != "Pending") return Task.FromResult<WhatsAppTaskNotification?>(null); Item.Status = "Processing"; Item.Attempts++; return Task.FromResult<WhatsAppTaskNotification?>(Item); }
    public Task<TaskWhatsAppData?> LoadAsync(WhatsAppTaskNotification item, CancellationToken token) => Task.FromResult(data);
    public Task CompleteAsync(WhatsAppTaskNotification item, Msg91Result result, CancellationToken token) { WhatsAppTaskQueue.ApplyResult(item,result); return Task.CompletedTask; }
}
sealed class TestSaveException : Exception { }
sealed class SaveProbe : SaveChangesInterceptor
{
    public bool Fail;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
    { if (Fail) throw new TestSaveException(); return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1)); }
}
