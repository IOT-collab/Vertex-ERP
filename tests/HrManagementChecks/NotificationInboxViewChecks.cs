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

internal static class NotificationInboxViewChecks
{
    public static async Task Run()
    {
        var root=Path.GetFullPath("Vertex ERP");
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(MainController).Assembly.GetName().Name,ContentRootPath=root,WebRootPath=Path.Combine(root,"wwwroot") });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MainController).Assembly);
        builder.Services.AddDbContext<ApplicationDbContext>(options=>options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); builder.Services.AddScoped<ManualAttendanceAccess>();
        builder.Services.AddScoped<ModuleAccessService>(provider=> { var access=new ModuleAccessService(provider.GetRequiredService<ApplicationDbContext>()); typeof(ModuleAccessService).GetField("states",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(access,new Dictionary<string,bool>()); return access; });
        await using var app=builder.Build();
        foreach (var role in new[]{"Employee","User","Manager","HR","Admin"})
        {
            using var scope=app.Services.CreateScope();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview"),new Claim(ClaimTypes.Role,role)},"view-test"));
            context.Features.Set<ISessionFeature>(new ExpenseViewChecks.TestSessionFeature { Session=new ExpenseViewChecks.EmptySession() });
            var model=new NotificationInboxViewModel { UnreadCount=1, Total=2, Items=new[]{
                new InboxNotification("Form:1:123:2","Feedback","Employee <script>alert(1)</script>","Form response uploaded · Version 2","/FeedbackDocuments/Details/1",DateTime.UtcNow),
                new InboxNotification("Leave:2:456:","Leave","Preview Employee · EL","Pending · Manager","/Main/ManagerLeaves",DateTime.UtcNow,true)
            } };
            var routes=new RouteData();routes.Values["controller"]="Notifications";routes.Values["action"]="Index";routes.Routers.Add(new ExpenseViewChecks.PreviewRouter());
            var action=new ActionContext(context,routes,new ActionDescriptor());
            var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/Notifications/Index.cshtml",true);
            if(!view.Success)throw new Exception("Feedback document view not found");
            var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) { Model=model };
            using var output=new StringWriter();
            await view.View.RenderAsync(new ViewContext(action,view.View,data,scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),output,new HtmlHelperOptions()));
            var html=output.ToString();
            if(System.Text.RegularExpressions.Regex.Matches(html,"data-notification-bell").Count != 1 || !html.Contains("notification-inbox.js")) throw new Exception(role+" must have exactly one working notification bell");
            if(!html.Contains("/Notifications/Open") || !html.Contains("/Notifications/MarkRead") || !html.Contains("__RequestVerificationToken")) throw new Exception("Notification actions or antiforgery missing");
            if(html.Contains("<script>alert(1)</script>")) throw new Exception("Notification text was not escaped");
            if(!html.Contains("Mark unread") || !html.Contains("Mark read") || !html.Contains("1 unread")) throw new Exception("Read/unread state missing");
            var directory=Path.GetFullPath("artifacts/notification-preview");Directory.CreateDirectory(directory);await System.IO.File.WriteAllTextAsync(Path.Combine(directory,role.ToLowerInvariant()+"-inbox.html"),html);
            Console.WriteLine("PASS "+role+" notification inbox render, single bell, actions, unread state and HTML escaping");
        }
    }
}
