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

internal static class EmployeeDocumentActionViewChecks
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
        foreach (var scenario in new[]{"update","missing-file","repository"})
        {
            using var scope=app.Services.CreateScope();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview"),new Claim(ClaimTypes.Role,"HR")},"view-test"));
            context.Features.Set<ISessionFeature>(new ExpenseViewChecks.TestSessionFeature { Session=new ExpenseViewChecks.EmptySession() });
            var employee=new Employee{Id=117,FullName="Document Employee",EmployeeCode="DOC-117",Department="Accounts",Designation="Executive"};
            object model=scenario=="repository" ? new EmployeeDocumentRepositoryViewModel{Employee=employee,Documents=new[]{new EmployeeDocument{Id=2,EmployeeId=117,DocumentType="Aadhaar Card",DocumentName="Aadhaar",OriginalFileName="aadhaar.pdf"}}}
                : new EmployeeDocumentUploadViewModel{DocumentId=2,EmployeeId=117,DocumentType="Aadhaar Card",DocumentName="Updated Aadhaar",CurrentFileName="aadhaar.pdf",CurrentFileMissing=scenario=="missing-file",Employees=new[]{new EmployeeDocumentEmployeeOption{Id=117,Name=employee.FullName,EmployeeCode=employee.EmployeeCode,Department=employee.Department,Designation=employee.Designation}}};
            var routes=new RouteData();routes.Values["controller"]="Hr";routes.Values["action"]=scenario=="repository"?"EmpDocuments":"UpdateDocument";routes.Routers.Add(new ExpenseViewChecks.PreviewRouter());
            var action=new ActionContext(context,routes,new ActionDescriptor());
            var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/Hr/"+(scenario=="repository"?"EmpDocuments":"UploadDocument")+".cshtml",true);
            if(!view.Success)throw new Exception("Feedback document view not found");
            var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) { Model=model };
            using var output=new StringWriter();
            await view.View.RenderAsync(new ViewContext(action,view.View,data,scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),output,new HtmlHelperOptions()));
            var html=output.ToString();
            if(!html.Contains("__RequestVerificationToken"))throw new Exception("Document action antiforgery missing");
            if(scenario=="repository" && (!html.Contains("/Hr/ViewDocument") || !html.Contains("/Hr/UpdateDocument") || !html.Contains("/Hr/DeleteDocument") || !html.Contains("employeeId=117")))throw new Exception("Document action links or employee context missing");
            if(scenario!="repository" && (!html.Contains("/Hr/UploadDocument") || !html.Contains("Save Document Changes") || !html.Contains("aadhaar.pdf")))throw new Exception("Document update form missing");
            if(scenario=="missing-file" && (!html.Contains("Replacement file (required)") || !html.Contains("restore this document")))throw new Exception("Missing-file recovery not visible");
            if(scenario=="update" && !html.Contains("Replacement file (optional)"))throw new Exception("Metadata-only update must remain possible");
            var directory=Path.GetFullPath("artifacts/document-action-preview");Directory.CreateDirectory(directory);await System.IO.File.WriteAllTextAsync(Path.Combine(directory,scenario+".html"),html);
            Console.WriteLine("PASS document "+scenario+" full Razor render and action controls");
        }
    }
}
