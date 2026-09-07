using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
var root=@"D:\vertex ERP\Vertex-ERP\Vertex ERP";
var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=args, WebRootPath=Path.Combine(root,"wwwroot") });
builder.Logging.ClearProviders(); builder.Logging.AddConsole();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddControllersWithViews();
// Render-only fixture host. No production controllers, database provider or writes.
builder.Services.AddSingleton(new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options));
builder.Services.AddSingleton(new PreviewRoot(root));
var app=builder.Build();
app.UseStaticFiles();
app.Use(async (context,next)=> { context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Preview Administrator"),new Claim(ClaimTypes.Role,"Admin")},"Preview")); await next(); });
app.MapControllers();
app.Run("http://127.0.0.1:8766");
public record PreviewRoot(string Path);
public class PreviewController(PreviewRoot root):Controller {
 [HttpGet("/{areaName:regex(^(Hr|Main|Employee|ProjectMgm)$)}/{page}")]
 public IActionResult Page(string areaName,string page) {
  if(areaName=="Employee"&&page=="Index"){areaName="Hr";page="EmployeeDashboard";}
  if(!Regex.IsMatch(areaName,"^[A-Za-z]+$")||!Regex.IsMatch(page,"^[A-Za-z]+$"))return NotFound();
  var file=System.IO.Path.Combine(root.Path,"Views",areaName,page+".cshtml");
  if(!System.IO.File.Exists(file))return NotFound();
  object? model=null;
  var source=System.IO.File.ReadAllText(file);
  var match=Regex.Match(source,@"@model\s+(VertexERP\.Models\.\w+)");
  if(match.Success)model=Activator.CreateInstance(typeof(Employee).Assembly.GetType(match.Groups[1].Value)!);
  var employee=new Employee{Id=1,FirstName="Example",LastName="Employee",EmployeeCode="EMP-001",Designation="Senior Operations Coordinator",Department="Engineering and Operations",Email="example@example.test"};
  switch(page){
   case "EmployeeDashboard":model=new EmployeeDirectoryViewModel{Employees=new[]{employee},Departments=new[]{"Engineering and Operations"},TotalEmployees=1,ActiveEmployees=1,TotalDepartments=1};break;
   case "Department":model=new DepartmentOverviewViewModel{Departments=new[]{new DepartmentOverviewItem{Id=1,Name="Engineering and Operations",Code="ENG-001",Description="Team supporting operations and delivery.",EmployeeCount=12,Status="Active",ManagerName="Example Manager"}},TotalDepartments=1,TotalEmployees=12};break;
   case "AssetManagement":model=new[]{new EmployeeAsset{Id=1,Employee=employee,EmployeeId=1,AssetTag="ASSET-001",AssetName="Business Laptop",Category="Laptop",SerialNumber="EXAMPLE-1234567890",IssueDate=DateOnly.FromDateTime(DateTime.Today),Notes="Sample asset details for layout verification"}};break;
   case "IssueAsset":model=new IssueAssetViewModel{Employees=new[]{employee}};break;
   case "DepartmentDetails":case "DeleteDepartment":model=new Department{DepartmentName="Engineering and Operations",DepartmentCode="ENG-001"};break;
   case "AddDepartment":model=new AddDepartmentViewModel{Managers=new[]{employee}};break;
   case "ExpenseClaim":
    var manager=new Employee{Id=2,FirstName="Priya",LastName="Manager",FullName="Priya Manager",EmployeeCode="MGR-002"};
    employee.FullName="Example Employee"; employee.ReportingManager=manager; employee.ReportingManagerId=manager.Id;
    model=new ExpenseClaimPageViewModel{Mode="Employee",Claims=new[]{
     new ExpenseClaim{Id=1,EmployeeId=1,Employee=employee,ReportingManagerId=2,ReportingManager=manager,Category="Travel",Title="Client visit cab fare",ExpenseDate=DateOnly.FromDateTime(DateTime.Today.AddDays(-2)),Amount=1450,Status="Pending",OriginalFileName="cab-receipt.pdf",StoredFileName="sample.pdf",ContentType="application/pdf",FileSize=1200,SubmittedAtUtc=DateTime.UtcNow.AddDays(-2)},
     new ExpenseClaim{Id=2,EmployeeId=1,Employee=employee,ReportingManagerId=2,ReportingManager=manager,Category="Food",Title="Team lunch during site visit",ExpenseDate=DateOnly.FromDateTime(DateTime.Today.AddDays(-5)),Amount=3200,Status="Approved",OriginalFileName="lunch-bill.jpg",StoredFileName="sample.jpg",ContentType="image/jpeg",FileSize=1200,SubmittedAtUtc=DateTime.UtcNow.AddDays(-5),DecidedAtUtc=DateTime.UtcNow.AddDays(-4),DecisionNote="Approved for reimbursement."}
    }};break;
   case "GeneratedEmployeeDocument":model=new EmployeeDocumentFormViewModel{EmployeeName="Example Employee",DocumentType="Offer Letter",Designation="Operations Coordinator",Department="Engineering",Email="example@example.test",ManagerName="Example Manager"};break;
   case "Hrms":model=new HrSummary();break;
  }
  return View("~/Views/"+areaName+"/"+page+".cshtml",model);
 }
}
public class HrSummary{public int TotalEmployees=>24;public int PresentToday=>20;public int OnLeaveToday=>2;public int AbsentToday=>2;}


