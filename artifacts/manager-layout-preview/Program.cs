using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
var source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Vertex ERP"));
// This isolated view renderer never reads a company database or runs production startup/migrations.
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(MainController).Assembly.GetName().Name,ContentRootPath=source,WebRootPath=Path.Combine(source,"wwwroot")});
builder.Configuration.Sources.Clear();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MainController).Assembly);
builder.Services.AddDbContext<ApplicationDbContext>(x=>x.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddScoped<VertexERP.Services.ModuleAccessService>(provider => {
 var access=new VertexERP.Services.ModuleAccessService(provider.GetRequiredService<ApplicationDbContext>());
 // Test fixture: all modules enabled, with no company database reads.
 typeof(VertexERP.Services.ModuleAccessService).GetField("states",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(access,new Dictionary<string,bool>());
 return access;
});
builder.Services.AddDistributedMemoryCache();builder.Services.AddSession();builder.Services.AddHttpContextAccessor();
var app=builder.Build();
app.Use(async(ctx,next)=>{if(ctx.Request.Path!="/preview"&&!new[]{"/css/","/js/","/images/","/lib/"}.Any(x=>ctx.Request.Path.StartsWithSegments(x.TrimEnd('/')))){ctx.Response.StatusCode=404;return;}await next();});
app.UseStaticFiles();app.UseSession();
app.MapGet("/preview",async context=>{
 context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Sushil Kumar Tyagi"),new Claim(ClaimTypes.NameIdentifier,"-1"),new Claim(ClaimTypes.Role,"Manager")},"layout-preview"));
 var names=new[]{"Bhoopendra Singh","Jaya Pal","Manoj Kumar","Pankaj Kumar","Peeyush Shrivastav","Satya Kumar","Yuvraj Singh"};
 var codes=new[]{"Vertex-24","A022","VPC0120","VAS0173","P038","VAS0146","VPC0163"};
 var members=names.Select((name,i)=>new Employee{Id=i+1,FullName=name,FirstName=name.Split(' ')[0],EmployeeCode=codes[i],Email="preview@example.invalid",PhoneNumber="—",Department=i==2||i==4?"HR & Administration":i==3?"Purchase / Procurement":"Accounts & Finance",Designation=i==2?"Office boy":i==3?"Purchase Executive":i==4?"Admin Executive":"Accounts Executive",IsActive=true}).ToList();
 var model=new ManagerDashboardViewModel{IsManagerScope=true,Viewer=new Employee{Id=-1,FullName="Sushil Kumar Tyagi",FirstName="Sushil"},TeamMembers=members};
 var routes=new RouteData();routes.Values["controller"]="Main";routes.Values["action"]="Manager";
 var action=new ActionContext(context,routes,new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
 var view=context.RequestServices.GetRequiredService<IRazorViewEngine>().GetView(null,"/Views/Main/Manager.cshtml",true);
 if(!view.Success)throw new Exception(string.Join(";",view.SearchedLocations));
 var data=new ViewDataDictionary<ManagerDashboardViewModel>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=model};
 var temp=context.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context);
 using var output=new StringWriter();
 await view.View.RenderAsync(new ViewContext(action,view.View,data,temp,output,new HtmlHelperOptions()));
 context.Response.ContentType="text/html; charset=utf-8";
 // Test-only deterministic light palette. Product appearance remains user-controlled.
 var html=output.ToString().Replace("<head>", "<head><script>localStorage.setItem('vertex.theme.v1.-1',JSON.stringify({mode:'light'}));</script>");
 await context.Response.WriteAsync(html);
});
app.MapControllerRoute("default","{controller=Main}/{action=Index}/{id?}");
app.Run("http://127.0.0.1:5188");
