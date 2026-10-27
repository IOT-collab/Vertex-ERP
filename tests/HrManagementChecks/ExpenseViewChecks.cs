using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

internal static class ExpenseViewChecks
{
    public static async Task Run()
    {
        var source = Path.GetFullPath("Vertex ERP");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MainController).Assembly.GetName().Name, ContentRootPath = source, WebRootPath = Path.Combine(source,"wwwroot") });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MainController).Assembly);
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddScoped<ManualAttendanceAccess>();
        builder.Services.AddScoped<ModuleAccessService>(provider => {
            var access = new ModuleAccessService(provider.GetRequiredService<ApplicationDbContext>());
            typeof(ModuleAccessService).GetField("states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(access, new Dictionary<string,bool>());
            return access;
        });
        await using var app = builder.Build();
        app.MapControllerRoute("default", "{controller=Main}/{action=Index}/{id?}");
        foreach (var mode in new[] { "Self", "Manager", "HR", "HRReviewed", "Recipient", "SelfResponded", "RecipientResponded" })
        {
            using var scope = app.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = "http"; context.Request.Host = new HostString("localhost"); context.Request.Path = "/Expense";
            // No subject is provided: layout account lookups short-circuit; no company DB is used.
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Preview Employee"), new Claim(ClaimTypes.Role, "Employee") }, "view-test"));
            context.Features.Set<ISessionFeature>(new TestSessionFeature { Session = new EmptySession() });
            var claim = new ExpenseClaim { Id = 1, EmployeeId = 10, Employee = new Employee { Id = 10, FullName = "Preview Employee", EmployeeCode = "PREVIEW-10" }, ReportingManagerId = 20,
                ReportingManager = new Employee { Id = 20, FullName = "Preview Manager" }, Category = "Travel", Title = "Client visit travel", Amount = 750, ExpenseDate = new(2026,10,20), OriginalFileName = "bill.pdf", ContentType = "application/pdf",
                Status = mode == "HRReviewed" ? "Approved" : "Pending", DecidedAtUtc = mode == "HRReviewed" ? DateTime.UtcNow : null,
                WorkflowStage = mode == "HR" ? "HR" : mode == "HRReviewed" ? "Reviewed" : mode == "Recipient" ? "Forwarded" : "Manager" };
            claim.ForwardedToEmployeeId = 30;
            if (mode.EndsWith("Responded")) {
                claim.WorkflowStage = "Completed"; claim.Status = "Rejected";
                claim.HrDecision = "Approved"; claim.DecidedAtUtc = DateTime.UtcNow;
                claim.RecipientDecision = "Rejected"; claim.RecipientReason = "Final response test reason";
                claim.RecipientRespondedAtUtc = DateTime.UtcNow; claim.ResponseStoredFileName = "reply.pdf";
                claim.ResponseOriginalFileName = "response-proof.pdf"; claim.ResponseContentType = "application/pdf";
            }
            var model = new ExpenseClaimPageViewModel { Mode = mode == "HRReviewed" ? "HR" : mode == "SelfResponded" ? "Self" : mode == "RecipientResponded" ? "Recipient" : mode, CurrentEmployeeId = mode.StartsWith("Self") ? 10 : mode == "Manager" ? 20 : 30,
                CanReceiveForwardedClaims = mode.StartsWith("Recipient"), HasReportingManager = true, ReportingManagerName = "Preview Manager", Claims = new[] { claim } };
            var routes = new RouteData(); routes.Values["controller"] = "Expense"; routes.Values["action"] = "Index"; routes.Routers.Add(new PreviewRouter());
            var action = new ActionContext(context, routes, new ActionDescriptor());
            var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, "/Views/Hr/ExpenseClaim.cshtml", true);
            if (!view.Success) throw new Exception("Expense view could not be resolved");
            var data = new ViewDataDictionary<ExpenseClaimPageViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
            var temp = scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context);
            using var output = new StringWriter();
            await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, output, new HtmlHelperOptions()));
            var html = output.ToString();
            if (!html.Contains("Client visit travel") || !html.Contains("Advance Payments")) throw new Exception("Expense route/advance partial rendering failed: " + mode);
            if (mode == "Self" && (!html.Contains("multipart/form-data") || !html.Contains("ReceiptFile") || !html.Contains("action=\"/Expense/Submit\""))) throw new Exception("Expense upload form missing");
            if (mode == "HRReviewed" && !html.Contains("action=\"/Expense/Forward\"")) throw new Exception("HR forward button is missing after review");
            if (mode != "HRReviewed" && html.Contains("action=\"/Expense/Forward\"")) throw new Exception("Forward button exposed before HR review or to wrong viewer");
            if (mode == "Recipient" && (!html.Contains("action=\"/Expense/Respond\"") || !html.Contains("name=\"ResponseFile\""))) throw new Exception("Yuvraj response upload form is missing");
            if (mode != "Recipient" && html.Contains("action=\"/Expense/Respond\"")) throw new Exception("Response form exposed to wrong user or after completion");
            if (mode.EndsWith("Responded") && (!html.Contains("Final response test reason") || !html.Contains("response-proof.pdf") || !html.Contains("/Expense/ResponseAttachment"))) throw new Exception("Employee final response or attachment link is missing");
            var folder = Path.GetFullPath("artifacts/expense-preview"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, mode.ToLowerInvariant() + ".html"), html);
            Console.WriteLine("PASS /Expense view and absolute advance partial render: " + mode);
        }
    }
    private sealed class PreviewRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
        public VirtualPathData? GetVirtualPath(VirtualPathContext context)
        {
            var controller = context.Values.GetValueOrDefault("controller")?.ToString() ?? "Expense";
            var action = context.Values.GetValueOrDefault("action")?.ToString() ?? "Index";
            var path = controller + "/" + action;
            var extra = context.Values.Where(item => item.Key is not ("controller" or "action") && item.Value != null)
                .Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value!.ToString()!));
            var query = string.Join("&", extra);
            return new VirtualPathData(this, path + (query.Length > 0 ? "?" + query : ""));
        }
    }
    private sealed class TestSessionFeature : ISessionFeature { public ISession Session { get; set; } = null!; }
    private sealed class EmptySession : ISession
    {
        public bool IsAvailable => true; public string Id => "test"; public IEnumerable<string> Keys => Array.Empty<string>();
        public void Clear() { } public void Remove(string key) { } public void Set(string key, byte[] value) { }
        public bool TryGetValue(string key, out byte[] value) { value = Array.Empty<byte>(); return false; }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
