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

internal static class FeedbackDocumentViewChecks
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
        var employee=new Employee { Id=7,FullName="Preview Employee",EmployeeCode="PREVIEW-7",Department="Accounts",Designation="Executive" };
        var document=new FeedbackDocument { Id=1,Title="Employee declaration",Instructions="Complete and sign this form." };
        document.Assets.Add(new FeedbackDocumentAsset { Id=1,DocumentId=1,OriginalFileName="declaration.pdf",ContentType="application/pdf",FileSize=1024 });
        var submission=new FeedbackDocumentSubmission { Id=2,DocumentId=1,EmployeeId=7,Employee=employee,Comment="Completed and signed",Revision=2 };
        submission.Assets.Add(new FeedbackDocumentAsset { Id=2,DocumentId=1,SubmissionId=2,OriginalFileName="signed-response.pdf",Revision=2,ContentType="application/pdf",FileSize=2048 });
        foreach(var scenario in new[]{"hr-index","employee-index","hr-details","employee-details"})
        {
            using var scope=app.Services.CreateScope();
            var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider };
            context.Request.Scheme="http";context.Request.Host=new HostString("localhost");context.Request.Path="/FeedbackDocuments";
            context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview Employee"),new Claim(ClaimTypes.Role,"Employee")},"view-test"));
            context.Features.Set<ISessionFeature>(new ExpenseViewChecks.TestSessionFeature { Session=new ExpenseViewChecks.EmptySession() });
            var management=scenario.StartsWith("hr"); var index=scenario.EndsWith("index");
            object model=index ? new FeedbackDocumentsViewModel { IsManagement=management,TotalEmployees=2,Documents=new[]{new FeedbackDocumentCard { Document=document,OwnSubmission=submission,SubmittedCount=1 }} }
                : new FeedbackDocumentDetailsViewModel { IsManagement=management,CanSubmit=!management,Document=document,OwnSubmission=management ? null : submission,
                    TotalEmployees=2,SubmittedCount=1,Documents=new[]{document},Departments=new[]{"Accounts"},Employees=new[]{new FeedbackDocumentEmployeeRow(employee,submission),new FeedbackDocumentEmployeeRow(new Employee{Id=8,FullName="Pending Employee",EmployeeCode="PREVIEW-8",Department="IT",Designation="Engineer"},null)} };
            var routes=new RouteData();routes.Values["controller"]="FeedbackDocuments";routes.Values["action"]=index?"Index":"Details";routes.Routers.Add(new ExpenseViewChecks.PreviewRouter());
            var action=new ActionContext(context,routes,new ActionDescriptor());
            var view=scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/FeedbackDocuments/"+(index?"Index":"Details")+".cshtml",true);
            if(!view.Success)throw new Exception("Feedback document view not found");
            var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) { Model=model };
            using var output=new StringWriter();
            await view.View.RenderAsync(new ViewContext(action,view.View,data,scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),output,new HtmlHelperOptions()));
            var html=output.ToString();
            if(!html.Contains("declaration.pdf") || !html.Contains("/FeedbackDocuments/File"))throw new Exception("Published document download missing");
            if(scenario=="hr-index"&&!html.Contains("/FeedbackDocuments/Publish"))throw new Exception("HR publication form missing");
            if(!management&&html.Contains("/FeedbackDocuments/Publish"))throw new Exception("Publication form exposed to employee");
            if(scenario=="hr-details"&&(!html.Contains("Pending Employee")||!html.Contains("signed-response.pdf")||!html.Contains("Executive")))throw new Exception("HR response table missing employee profile, pending row or attachment");
            if(scenario=="employee-details"&&(!html.Contains("/FeedbackDocuments/Submit")||!html.Contains("signed-response.pdf")))throw new Exception("Employee response form/history missing");
            if (!management && html.Contains("Pending Employee")) throw new Exception("Other employee profile exposed in personal view");
            var directory=Path.GetFullPath("artifacts/feedback-preview");Directory.CreateDirectory(directory);await System.IO.File.WriteAllTextAsync(Path.Combine(directory,scenario+".html"),html);
            Console.WriteLine("PASS feedback document full page render: "+scenario);
        }
    }
}
