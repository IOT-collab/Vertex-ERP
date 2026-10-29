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

internal static class SiteLocationCardViewChecks
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
        foreach (var scenario in new[]{"empty","populated"})
        {
            using var scope=app.Services.CreateScope();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview"),new Claim(ClaimTypes.Role,"HR")},"view-test"));
            context.Features.Set<ISessionFeature>(new ExpenseViewChecks.TestSessionFeature { Session=new ExpenseViewChecks.EmptySession() });
            var employee=new SiteEmployeeLocationItem{EmployeeId=8,EmployeeName="Site Employee",EmployeeCode="SITE-8",Department="Projects",Designation="Engineer",PhotoPath="/profile-test.jpg",CheckInTime=new DateTime(2026,10,29,9,30,0),CheckInSiteName="Test Site",CheckInLatitude=28,CheckInLongitude=77,CheckInAccuracyMetres=5,CheckInSelfiePath="/selfie-test.jpg",LastLocationTime=new DateTime(2026,10,29,9,30,0),LastLatitude=28,LastLongitude=77};
            var model=new SiteEmployeeLocationViewModel{SelectedDate=new DateOnly(2026,10,29),Employees=scenario=="empty"?[]:new[]{employee},SelectedEmployee=scenario=="empty"?null:employee};
            var routes=new RouteData();routes.Values["controller"]="Main";routes.Values["action"]="LocationTracking";routes.Routers.Add(new ExpenseViewChecks.PreviewRouter());
            var action=new ActionContext(context,routes,new ActionDescriptor());
            var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/Main/LocationTrackingLive.cshtml",true);
            if(!view.Success)throw new Exception("Feedback document view not found");
            var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) { Model=model };
            using var output=new StringWriter();
            await view.View.RenderAsync(new ViewContext(action,view.View,data,scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),output,new HtmlHelperOptions()));
            var html=output.ToString();
            if(!html.Contains("site-location-cards.js") || !html.Contains("data-site-location-content"))throw new Exception("Site refresh hooks missing");
            if(scenario=="empty" && (!html.Contains("No site attendance found")||html.Contains("data-site-employee=")))throw new Exception("Empty site roster must not list unrelated employees");
            if(scenario=="populated" && (!html.Contains("/profile-test.jpg")||!html.Contains("Test Site")||!html.Contains("/selfie-test.jpg")||!html.Contains("google.com/maps")||!html.Contains("data-site-employee=\"8\"")))throw new Exception("Site profile, punch details, map or selfie missing");
            var directory=Path.GetFullPath("artifacts/site-location-preview");Directory.CreateDirectory(directory);await System.IO.File.WriteAllTextAsync(Path.Combine(directory,scenario+".html"),html);
            Console.WriteLine("PASS site location "+scenario+" full page render");
        }
    }
}
