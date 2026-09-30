using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Security.Claims;
using Npgsql;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Controllers;
var config=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("Vertex ERP/appsettings.json")).AddJsonFile(Path.GetFullPath("Vertex ERP/appsettings.Development.json"),true).Build();
var connection=new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));var schema="leave_rejection_check_"+Guid.NewGuid().ToString("N");
await using var admin=new NpgsqlConnection(connection.ConnectionString);await admin.OpenAsync();await new NpgsqlCommand($"CREATE SCHEMA {schema}",admin).ExecuteNonQueryAsync();
try{
 connection.SearchPath=schema;await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options);await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
 Employee Employee(string code)=>new(){EmployeeCode=code,FirstName=code,FullName=code,Email=code+"@example.invalid",PhoneNumber=code,Department="Test",Designation="Test",JoiningDate=new(2026,1,1)};
 var manager=Employee("M1");var otherManager=Employee("M2");var employee=Employee("E1");db.AddRange(manager,otherManager,employee);await db.SaveChangesAsync();
 var user=new AppUser{EmployeeId=manager.Id,Username="manager",NormalizedUsername="MANAGER",FullName="Manager",Role="Manager",IsActive=true,PasswordHash="test"};db.AppUsers.Add(user);await db.SaveChangesAsync();
 LeaveRequest Request(int assigned)=>new(){EmployeeId=employee.Id,AssignedApproverEmployeeId=assigned,LeaveType="CL",FromDate=new(2026,9,26),ToDate=new(2026,9,27),Reason="Test",Status="Pending"};
 var reject=Request(manager.Id);var approve=Request(manager.Id);var outside=Request(otherManager.Id);var invalid=Request(manager.Id);db.LeaveRequests.AddRange(reject,approve,outside,invalid);db.ManualLeaveBalances.Add(new(){EmployeeId=employee.Id,Year=2026,Category="CL",TotalDays=8,UsedDays=3,UpdatedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
 MainController Controller(){var http=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,"Manager")},"test"))};return new MainController(db,null!,null!,null!,null!,null!){ControllerContext=new ControllerContext{HttpContext=http},TempData=new TempDataDictionary(http,new MemoryTempData())};}
 var controller=Controller();var list=(ManagerSectionViewModel)((ViewResult)await controller.ManagerLeaves()).Model!;
 if(list.LeaveRequests.Count!=3||list.LeaveRequests.Any(x=>x.Id==outside.Id))throw new Exception("Manager list authorization mismatch");
 if(await controller.DecideTeamLeave(reject.Id,"Rejected",null) is not RedirectToActionResult)throw new Exception("Reject returned error");
 db.ChangeTracker.Clear();var saved=await db.LeaveRequests.SingleAsync(x=>x.Id==reject.Id);
 if(saved.Status!="Rejected"||saved.DecidedByUserId!=user.Id||saved.DecidedAtUtc==null)throw new Exception("Rejection not persisted");
 await Controller().DecideTeamLeave(reject.Id,"Approved",null);
 if((await db.LeaveRequests.AsNoTracking().SingleAsync(x=>x.Id==reject.Id)).Status!="Rejected")throw new Exception("Repeated decision overwrote rejection");
 await Controller().DecideTeamLeave(approve.Id,"Approved",null);await Controller().DecideTeamLeave(invalid.Id,null,null);await Controller().DecideTeamLeave(outside.Id,"Rejected",null);
 if((await db.LeaveRequests.AsNoTracking().SingleAsync(x=>x.Id==approve.Id)).Status!="Approved")throw new Exception("Approve regression");
 if(await db.LeaveRequests.CountAsync(x=>(x.Id==outside.Id||x.Id==invalid.Id)&&x.Status=="Pending")!=2)throw new Exception("Unauthorized/invalid decision changed data");
 if((await db.ManualLeaveBalances.SingleAsync()).UsedDays!=3)throw new Exception("Manual balance changed");
 list=(ManagerSectionViewModel)((ViewResult)await Controller().ManagerLeaves()).Model!;
 if(list.LeaveRequests.Count(x=>x.Status=="Rejected")!=1)throw new Exception("Rejected page reload failed");
 Console.WriteLine("PASS: reject saved with reviewer/time, list reload, approve, repeated reject, missing decision, manager isolation, unchanged manual leave balances.");
}finally{await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE",admin).ExecuteNonQueryAsync();}
class MemoryTempData:ITempDataProvider{public IDictionary<string,object> LoadTempData(HttpContext c)=>new Dictionary<string,object>();public void SaveTempData(HttpContext c,IDictionary<string,object> v){}}
