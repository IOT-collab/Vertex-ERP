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

internal static class HrLeaveViewChecks
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
        foreach (var role in new[]{"HR","Admin"})
        {
            using var scope=app.Services.CreateScope();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview"),new Claim(ClaimTypes.Role,role)},"view-test"));
            context.Features.Set<ISessionFeature>(new ExpenseViewChecks.TestSessionFeature { Session=new ExpenseViewChecks.EmptySession() });
            var employee=new Employee{Id=8,FullName="Leave Employee",EmployeeCode="LEAVE-8",Department="Accounts"};
            var request=new LeaveRequest{Id=1,EmployeeId=8,Employee=employee,LeaveType="EL",Reason="Employee informed HR",FromDate=new DateOnly(2026,10,20),ToDate=new DateOnly(2026,10,21),SubmittedByHrUserId=2,SubmittedByHrName="HR Submitter",AssignedApproverEmployee=new Employee{Id=7,FullName="Reporting Manager"}};
            var model=new HrLeavePage{CanSubmit=role=="HR",Employees=new[]{new HrLeaveEmployeeOption(8,employee.FullName,employee.EmployeeCode,employee.Department,"Reporting Manager")},Requests=new[]{request}};
            var routes=new RouteData();routes.Values["controller"]="HrLeave";routes.Values["action"]="Index";routes.Routers.Add(new ExpenseViewChecks.PreviewRouter());
            var action=new ActionContext(context,routes,new ActionDescriptor());
            var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/HrLeave/Index.cshtml",true);
            if(!view.Success)throw new Exception("Feedback document view not found");
            var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) { Model=model };
            using var output=new StringWriter();
            await view.View.RenderAsync(new ViewContext(action,view.View,data,scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),output,new HtmlHelperOptions()));
            var html=output.ToString();
            if(!html.Contains("Leave Employee") || !html.Contains("HR Submitter") || !html.Contains("Applied by HR on behalf")) throw new Exception("HR source or employee history missing");
            if(role=="HR" && (!html.Contains("id=\"hr-leave-form\"") || !html.Contains("__RequestVerificationToken") || !html.Contains("/HrLeave/Apply") || !html.Contains("1st Half") || !html.Contains("2nd Half"))) throw new Exception("HR leave form or session choices missing");
            if(role=="Admin" && (html.Contains("id=\"hr-leave-form\"") || html.Contains("Submit to Reporting Manager"))) throw new Exception("Admin must be view only");
            var directory=Path.GetFullPath("artifacts/hr-leave-preview");Directory.CreateDirectory(directory);await System.IO.File.WriteAllTextAsync(Path.Combine(directory,role.ToLowerInvariant()+".html"),html);
            Console.WriteLine("PASS "+role+" on-behalf leave page render and permission-specific controls");
        }
    }
}
