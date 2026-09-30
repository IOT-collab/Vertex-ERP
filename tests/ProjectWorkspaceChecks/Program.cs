using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

// Uses only the dedicated local test cluster, never application connection settings.
var database = "project_checks_" + Guid.NewGuid().ToString("N");
var connection = $"Host=127.0.0.1;Port=55439;Database={database};Username=project_test";
var root = Path.GetFullPath("Vertex ERP");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(ProjectWorkspaceController).Assembly.GetName().Name, ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot") });
builder.Logging.ClearProviders();
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ModuleAccessService>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(".artifacts/project-test-keys")));
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProjectWorkspaceController).Assembly);
builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=ProjectWorkspace}/{action=Index}/{id?}");

var today = DateOnly.FromDateTime(DateTime.Today);
int employeeId, secondEmployeeId, outsideId, managerId, managerUserId, otherManagerUserId, departmentId;
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    Check(!db.Database.HasPendingModelChanges(), "Migrations match the application model");
    Employee Person(string code) => new() { EmployeeCode = code, FirstName = code, LastName = "Test", FullName = code + " Test", Email = code + "@example.test", PhoneNumber = code, Department = "Engineering", Designation = "Engineer", JoiningDate = today.AddDays(-30) };
    var manager = Person("ManagerA"); var otherManager = Person("ManagerB"); var employee = Person("EmployeeA"); var second = Person("EmployeeB"); var outside = Person("Outside");
    db.Employees.AddRange(manager, otherManager, employee, second, outside);
    var department = new Department { DepartmentCode = "ENG", DepartmentName = "Engineering" }; db.Departments.Add(department);
    await db.SaveChangesAsync();
    AppUser Account(Employee person) => new() { EmployeeId = person.Id, Username = person.EmployeeCode, NormalizedUsername = person.EmployeeCode.ToUpperInvariant(), FullName = person.FullName, PasswordHash = "test-only-unused", Role = "Manager" };
    var user = Account(manager); var otherUser = Account(otherManager); db.AppUsers.AddRange(user, otherUser); await db.SaveChangesAsync();
    employeeId = employee.Id; secondEmployeeId = second.Id; outsideId = outside.Id; managerId = manager.Id; managerUserId = user.Id; otherManagerUserId = otherUser.Id; departmentId = department.Id;
}
app.Urls.Add("http://127.0.0.1:55440");
await app.StartAsync();
HttpClient Client(string role, int userId) { var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("http://127.0.0.1:55440") }; client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString()); return client; }
using var admin = Client("Admin", 999); using var managerClient = Client("Manager", managerUserId); using var other = Client("Manager", otherManagerUserId); using var unlinked = Client("Manager", 9999);
async Task<HttpResponseMessage> Post(HttpClient client, string action, string section, params (string key, object value)[] fields)
{
    var html = await client.GetStringAsync("/ProjectWorkspace/Index?section=" + section);
    var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    Check(token.Length > 0, "Form includes anti-forgery token: " + section);
    var data = fields.Select(f => new KeyValuePair<string, string>(f.key, Convert.ToString(f.value, System.Globalization.CultureInfo.InvariantCulture)!)).ToList();
    data.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(token)));
    var response = await client.PostAsync("/ProjectWorkspace/" + action, new FormUrlEncodedContent(data));
    if (response.StatusCode == HttpStatusCode.InternalServerError) throw new Exception("Server failure: " + action + " " + await response.Content.ReadAsStringAsync());
    return response;
}
async Task<T> Read<T>(Func<ApplicationDbContext, Task<T>> query) { await using var scope = app.Services.CreateAsyncScope(); return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()); }
string Day(DateOnly day) => day.ToString("yyyy-MM-dd");

var response = await Post(admin, "SaveTeam", "teams", ("TeamForm.Name", "Delivery Team"), ("TeamForm.IsActive", "true"), ("memberIds", employeeId), ("memberIds", secondEmployeeId));
Check(response.StatusCode == HttpStatusCode.Redirect, "HR/Admin can create named employee teams");
var teamId = await Read(db => db.ProjectTeams.Select(t => t.Id).SingleAsync());
response = await Post(admin, "SaveTeam", "teams", ("TeamForm.Name", "delivery team"), ("TeamForm.IsActive", "true"), ("memberIds", employeeId));
Check(response.StatusCode == HttpStatusCode.OK && await Read(db => db.ProjectTeams.CountAsync()) == 1, "Team names reject case-only duplicates");
response = await Post(admin, "SaveProject", "projects", ("ProjectForm.ProjectCode", "PRJ-001"), ("ProjectForm.ProjectName", "Factory Automation"), ("ProjectForm.DepartmentId", departmentId), ("ProjectForm.ManagerId", managerId), ("ProjectForm.TeamId", teamId), ("ProjectForm.Budget", "10000"), ("ProjectForm.StartDate", Day(today.AddDays(-7))), ("ProjectForm.EndDate", Day(today.AddDays(30))), ("ProjectForm.Status", "Active"));
if (response.StatusCode != HttpStatusCode.Redirect) Console.WriteLine(await response.Content.ReadAsStringAsync());
Check(response.StatusCode == HttpStatusCode.Redirect, "Project creation saves manager, team, dates and budget");
var projectId = await Read(db => db.Projects.Select(p => p.Id).SingleAsync());
Check(await Read(db => db.ProjectEmployees.CountAsync(a => a.ProjectId == projectId)) == 2, "Project creation allocates the selected team's employees");
Check((await other.GetAsync($"/ProjectWorkspace/Index?projectId={projectId}")).StatusCode == HttpStatusCode.NotFound, "Another manager cannot read the project");
Check(!(await unlinked.GetStringAsync("/ProjectWorkspace/Index")).Contains("Factory Automation"), "Manager without employee mapping sees no projects");

(string key, object value)[] TaskFields(int assignee, string status = "To Do", int id = 0) => [("TaskForm.Id", id), ("TaskForm.ProjectId", projectId), ("TaskForm.AssigneeId", assignee), ("TaskForm.Title", "Install control panel"), ("TaskForm.Status", status), ("TaskForm.Priority", "High"), ("TaskForm.DueDate", Day(today.AddDays(5)))];
response = await Post(managerClient, "SaveTask", "tasks", TaskFields(employeeId));
Check(response.StatusCode == HttpStatusCode.Redirect, "Assigned manager can assign project task to an allocated employee");
var taskId = await Read(db => db.WorkTasks.Select(t => t.Id).SingleAsync());
response = await Post(managerClient, "SaveTask", "tasks", TaskFields(outsideId));
Check(response.StatusCode == HttpStatusCode.OK && await Read(db => db.WorkTasks.CountAsync()) == 1, "Unallocated employee task is rejected");
response = await Post(other, "SaveTask", "tasks", TaskFields(employeeId));
Check(response.StatusCode == HttpStatusCode.NotFound, "Another manager cannot write project tasks");
response = await Post(managerClient, "SaveTask", "tasks", TaskFields(employeeId, "Invalid"));
Check(response.StatusCode == HttpStatusCode.OK && await Read(db => db.WorkTasks.CountAsync()) == 1, "Invalid task status is rejected by model validation");
response = await Post(admin, "Allocate", "resources", ("projectId", projectId), ("memberIds", secondEmployeeId));
Check(response.StatusCode == HttpStatusCode.OK && await Read(db => db.ProjectEmployees.CountAsync()) == 2, "Cannot remove an employee with open tasks");

response = await Post(managerClient, "SaveTime", "timesheets", ("TimeForm.ProjectId", projectId), ("TimeForm.EmployeeId", employeeId), ("TimeForm.WorkDate", Day(today)), ("TimeForm.Hours", "8"), ("TimeForm.Notes", "Panel assembly"));
Check(response.StatusCode == HttpStatusCode.Redirect, "Manager can record project work hours");
response = await Post(managerClient, "SaveTime", "timesheets", ("TimeForm.ProjectId", projectId), ("TimeForm.EmployeeId", employeeId), ("TimeForm.WorkDate", Day(today)), ("TimeForm.Hours", "17"), ("TimeForm.Notes", "Invalid total"));
Check(response.StatusCode == HttpStatusCode.OK && await Read(db => db.ProjectTimeEntries.SumAsync(t => t.Hours)) == 8, "Daily employee hours cannot exceed 24");
response = await Post(admin, "SaveCost", "budget", ("CostForm.ProjectId", projectId), ("CostForm.Title", "Control components"), ("CostForm.Amount", "1250.50"), ("CostForm.ExpenseDate", Day(today)));
Check(response.StatusCode == HttpStatusCode.Redirect && await Read(db => db.ProjectCosts.SumAsync(c => c.Amount)) == 1250.50m, "Project cost persists with decimal precision");
response = await Post(managerClient, "SaveCost", "budget", ("CostForm.ProjectId", projectId), ("CostForm.Title", "Not authorized"), ("CostForm.Amount", "2"));
Check(response.StatusCode == HttpStatusCode.Forbidden, "Manager cannot change project financial records");
response = await Post(managerClient, "SaveEnquiry", "enquiries", ("EnquiryForm.ProjectId", projectId), ("EnquiryForm.Subject", "Drawing clarification"), ("EnquiryForm.Description", "Confirm cable specification"), ("EnquiryForm.Status", "Open"));
Check(response.StatusCode == HttpStatusCode.Redirect, "Project enquiries persist");
var enquiryId = await Read(db => db.ProjectEnquiries.Select(e => e.Id).SingleAsync());
response = await Post(managerClient, "SaveEnquiry", "enquiries", ("EnquiryForm.Id", enquiryId), ("EnquiryForm.ProjectId", projectId), ("EnquiryForm.Subject", "Drawing clarification"), ("EnquiryForm.Description", "Confirm cable specification"), ("EnquiryForm.Status", "Resolved"));
Check(response.StatusCode == HttpStatusCode.OK, "Resolution is required before closing an enquiry");
response = await Post(managerClient, "SaveEnquiry", "enquiries", ("EnquiryForm.Id", enquiryId), ("EnquiryForm.ProjectId", projectId), ("EnquiryForm.Subject", "Drawing clarification"), ("EnquiryForm.Description", "Confirm cable specification"), ("EnquiryForm.Status", "Resolved"), ("EnquiryForm.Resolution", "Specification confirmed"));
Check(response.StatusCode == HttpStatusCode.Redirect, "Manager can resolve an enquiry with recorded resolution");
response = await Post(managerClient, "SaveTask", "tasks", TaskFields(employeeId, "Completed", taskId));
Check(response.StatusCode == HttpStatusCode.Redirect, "Task status updates persist");
Check((await managerClient.GetStringAsync("/ProjectWorkspace/Index?section=timeline")).Contains("100% tasks complete"), "Timeline computes progress from actual task status");
Check((await admin.GetStringAsync("/ProjectWorkspace/Index?section=budget")).Contains("8,749.50"), "Budget shows actual remaining amount");
foreach (var section in new[] { "projects", "teams", "tasks", "timeline", "resources", "timesheets", "budget", "enquiries" })
    Check((await admin.GetAsync("/ProjectWorkspace/Index?section=" + section)).IsSuccessStatusCode, "Module renders: " + section);
foreach (var entity in new[] { "ProjectTeam", "ProjectTeamMember", "ErpProject", "ProjectEmployee", "WorkTask", "ProjectTimeEntry", "ProjectCost", "ProjectEnquiry" })
    Check(await Read(db => db.AuditLogs.AnyAsync(a => a.EntityType == entity)), "Persistent audit coverage: " + entity);
Check((await admin.PostAsync("/ProjectWorkspace/DeleteEntry", new FormUrlEncodedContent(new Dictionary<string,string> { ["section"]="tasks", ["id"]=taskId.ToString() }))).StatusCode == HttpStatusCode.BadRequest, "Mutations reject missing anti-forgery token");
Console.WriteLine("ALL PROJECT WORKSPACE CHECKS PASSED");
if (args.Contains("--serve")) { Console.WriteLine("Preview: http://127.0.0.1:55440/ProjectWorkspace/Index"); await app.WaitForShutdownAsync(); }
else await app.StopAsync();

static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Admin";
        var id = Request.Headers["X-Test-User"].FirstOrDefault() ?? "999";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,id), new Claim(ClaimTypes.Name,"Test " + role), new Claim(ClaimTypes.Role,role) }, "test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,"test")));
    }
}
