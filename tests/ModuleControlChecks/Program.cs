using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using VertexERP.TagHelpers;

void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: " + label); }
var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("Vertex ERP")).AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
// Isolate all mutations from the application database.
var testName = "vertex_module_test_" + Guid.NewGuid().ToString("N");
var adminConnection = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = "postgres" };
await using var admin = new NpgsqlConnection(adminConnection.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{testName}\"", admin)) await create.ExecuteNonQueryAsync();
connection.Database = testName;
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString, x => x.EnableRetryOnFailure()).Options;
var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "42"), new Claim(ClaimTypes.Name, "Module Test"), new Claim(ClaimTypes.Role, "Admin") }, "test")) };
ApplicationDbContext Db() => new(options, new HttpContextAccessor { HttpContext = http });
async Task Change(string id, bool active, long revision)
{
    await using var db = Db();
    var controller = new ModuleAdminController(db) { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new TestTempData()) };
    Check(await controller.Toggle(id, active, revision) is RedirectToActionResult, "Module transition returns to panel");
}
try
{
    await using (var db = Db())
    {
        await db.Database.MigrateAsync();
        Check(await db.ModuleStates.CountAsync() == ModuleCatalog.All.Length, "Migration seeds every module");
        Check(await db.ModuleStates.AllAsync(x => x.IsActive), "Existing installations start with modules active");
        db.Departments.Add(new Department { DepartmentName = "Preserved module test", DepartmentCode = "MODTEST" });
        await db.SaveChangesAsync();
    }
    await Change("attendance", false, 0);
    await using (var db = Db()) Check((await db.ModuleStates.FindAsync("attendance"))!.IsActive, "Active Payroll prevents disabling Attendance");
    await Change("payroll", false, 0);
    await Change("attendance", false, 0);
    await using (var db = Db())
    {
        var access = new ModuleAccessService(db);
        Check(!await access.AllowedAsync("Main", "ExportAttendance"), "Disabled module blocks export route");
        Check(!await access.AllowedAsync("BiometricDevices", "Index"), "Disabled module blocks device management API");
        Check(await access.AllowedAsync("Main", "AdminPanel") && await access.AllowedAsync("Main", "Login"), "Admin recovery and login remain accessible");
        var route = new RouteData(); route.Values["controller"] = "Main"; route.Values["action"] = "AddAttendance";
        var action = new ActionContext(http, route, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new ResourceExecutingContext(action, new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        var executed = false;
        await new ModuleAccessFilter(access).OnResourceExecutionAsync(context, () => { executed = true; return Task.FromResult(new ResourceExecutedContext(action, new List<IFilterMetadata>())); });
        Check(!executed && context.Result is ObjectResult { StatusCode: 403 }, "Resource filter rejects requests before controller execution");
        var output = new TagHelperOutput("a", new TagHelperAttributeList { { "href", "/Main/EmployeeAttendance" } }, (cached, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        await new ModuleLinkTagHelper(access).ProcessAsync(new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "test"), output);
        Check(output.IsContentModified && output.TagName == null, "Disabled module navigation is suppressed server-side");
        Check(await db.AuditLogs.AnyAsync(x => x.EntityType == "ModuleState" && x.ActorId == "42" && x.Detail.Contains("attendance") && x.Action.Contains("deactivated")), "Status and administrator saved in audit history");
    }
    await Change("payroll", true, 1);
    await using (var db = Db()) Check(!(await db.ModuleStates.FindAsync("payroll"))!.IsActive, "Payroll cannot activate while dependency is disabled");
    await Change("attendance", true, 0);
    await using (var db = Db()) Check(!(await db.ModuleStates.FindAsync("attendance"))!.IsActive, "Stale form cannot overwrite another change");
    await Change("attendance", true, 1);
    await using (var db = Db())
    {
        Check(await new ModuleAccessService(db).AllowedAsync("Main", "Attendence"), "Fresh request restores access after reactivation");
        Check(await db.Departments.AnyAsync(x => x.DepartmentCode == "MODTEST"), "Existing business records survive transitions");
    }
    Check(typeof(ModuleAdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "Admin", "Only Admin can change states");
    Check(Attribute.IsDefined(typeof(ModuleAdminController).GetMethod("Toggle")!, typeof(ValidateAntiForgeryTokenAttribute)), "Changes require anti-forgery validation");
    Check(ModuleCatalog.All.All(m => ModuleCatalog.ForRoute(m.Controller, m.Action) == m.Id), "Every module entry point is protected");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE \"{testName}\" WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
}
sealed class TestTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
