using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using Vertex_ERP.Controllers;

internal static class NotificationInboxChecks
{
    public static async Task Run()
    {
        var passed=0;
        void Check(bool ok,string message) { if(!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        var manager=new NotificationInboxScope("Manager",7);
        var leaves=new[]{new LeaveRequest{Id=1,EmployeeId=8,AssignedApproverEmployeeId=7},new LeaveRequest{Id=2,EmployeeId=9,AssignedApproverEmployeeId=10},new LeaveRequest{Id=3,EmployeeId=7,AssignedApproverEmployeeId=7}}.AsQueryable();
        Check(manager.Leaves(leaves).Select(x=>x.Id).SequenceEqual(new[]{1}),"Manager sees assigned leave only, excluding self and other teams");
        Check(new NotificationInboxScope("Manager",null).Leaves(leaves).Count()==0,"Unlinked manager cannot see requests");
        Check(new NotificationInboxScope("Employee",7).Leaves(leaves).Count()==0,"Employee role cannot read management inbox");
        Check(new NotificationInboxScope("HR",null).Leaves(leaves).Count()==0,"HR does not receive applications before manager review");
        var reviewed=new LeaveRequest{EmployeeId=8,ApprovalLevel="HR/Admin",ManagerDecision="Rejected",ManagerDecidedAtUtc=DateTime.UtcNow};
        Check(new NotificationInboxScope("HR",null).Leaves(new[]{reviewed}.AsQueryable()).Count()==1,"Manager rejection generates HR final-review notification");
        reviewed.ManagerDecision="Approved";
        Check(new NotificationInboxScope("HR",null).Leaves(new[]{reviewed}.AsQueryable()).Count()==1,"Manager approval generates HR final-review notification");
        Check(new NotificationInboxScope("Admin",null).Leaves(leaves).Count()==3,"Admin can see employee leave submissions");
        var queries=new[]{new QueryTicket{Id=1,EmployeeId=8,ReportingManagerId=7},new QueryTicket{Id=2,EmployeeId=9,ReportingManagerId=10},new QueryTicket{Id=3,EmployeeId=7,ReportingManagerId=7}}.AsQueryable();
        Check(manager.Queries(queries).Select(x=>x.Id).SequenceEqual(new[]{1}),"Query notifications restricted to reporting manager");
        var expenses=new[]{new ExpenseClaim{Id=1,EmployeeId=8,ReportingManagerId=7,WorkflowStage="Manager"},new ExpenseClaim{Id=2,EmployeeId=9,ReportingManagerId=10,WorkflowStage="HR"},new ExpenseClaim{Id=3,EmployeeId=7,ReportingManagerId=7,WorkflowStage="HR"}}.AsQueryable();
        Check(manager.Expenses(expenses).Select(x=>x.Id).SequenceEqual(new[]{1}),"Expense notifications follow team authorization");
        Check(new NotificationInboxScope("HR",null).Expenses(expenses).Select(x=>x.Id).SequenceEqual(new[]{2,3}),"HR expenses arrive after manager stage");
        Check(!manager.PrivateSubmissions && !manager.BankRequests,"Private feedback, documents and bank requests stay out of manager inbox");
        Check(new NotificationInboxScope("HR",null).BankRequests && !new NotificationInboxScope("Admin",null).BankRequests,"Bank notifications match existing HR-only screen permission");
        var reports=new[]{new WorkTask{Id=1,ManagerId=7,AssigneeId=8,UpdatedAtUtc=DateTime.UtcNow},new WorkTask{Id=2,ManagerId=10,AssigneeId=9,UpdatedAtUtc=DateTime.UtcNow},new WorkTask{Id=3,ManagerId=7,AssigneeId=8}}.AsQueryable();
        Check(manager.Reports(reports).Select(x=>x.Id).SequenceEqual(new[]{1}),"Only updated team tasks generate report notifications");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options);
        Check(manager.Leaves(db.LeaveRequests).ToQueryString().Contains("AssignedApproverEmployeeId"),"Leave scope translates to PostgreSQL");
        Check(manager.Expenses(db.ExpenseClaims).ToQueryString().Contains("ReportingManagerId"),"Expense scope translates to PostgreSQL");
        Check(manager.Reports(db.WorkTasks).ToQueryString().Contains("UpdatedAtUtc"),"Report scope translates to PostgreSQL");
        var migration=db.GetService<IMigrator>().GenerateScript();
        Check(migration.Contains("CREATE TABLE \"NotificationReadReceipts\"") && migration.Contains("FOREIGN KEY (\"UserId\") REFERENCES \"Users\""),"Read receipts reference actual Users table");
        Check(db.Model.FindEntityType(typeof(NotificationReadReceipt))!.GetIndexes().Any(x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"UserId","EventKey"})),"Read receipts unique per user and event");
        var service=new NotificationInboxService(db);
        Check((await service.LoadAsync(new ClaimsPrincipal(new ClaimsIdentity()))).Count==0,"Anonymous identity causes no database access or data exposure");
        var controller=new NotificationsController(service,db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        Check(await controller.Index(null) is ForbidResult,"Notification inbox rejects missing account identity");
        Check(await controller.Open("guessed-key",default) is ForbidResult,"Guessed notification cannot be opened without identity");
        foreach(var name in new[]{"Open","MarkRead"}) Check(typeof(NotificationsController).GetMethod(name)!.IsDefined(typeof(HttpPostAttribute),true)&&typeof(NotificationsController).GetMethod(name)!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),name+" requires POST and antiforgery");
        Check(typeof(NotificationsController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles=="HR,Admin,Manager,Employee,User","Notification endpoints allow all supported account roles");
        var personal = new NotificationInboxScope("Employee",8);
        Check(personal.Personal && !personal.Management && !personal.Manager, "Employee has personal scope without management privileges");
        Check(!new NotificationInboxScope("User",null).Personal && !new NotificationInboxScope("Admin",8).Personal, "Unlinked accounts and Admin cannot use employee-only destinations");
        var assignments = new[]{new WorkTask{Id=1,AssigneeId=8},new WorkTask{Id=2,AssigneeId=9}}.AsQueryable();
        Check(personal.OwnTasks(assignments).Select(x=>x.Id).SequenceEqual(new[]{1}), "Employee sees assigned tasks only, including newly created tasks");
        Check(new NotificationInboxScope("User",8).OwnTasks(assignments).Count()==1, "Legacy User role receives employee assignments");
        var replies = new[]{new LeaveRequest{Id=1,EmployeeId=8,ManagerDecidedAtUtc=DateTime.UtcNow},new LeaveRequest{Id=2,EmployeeId=9,Status="Approved"},new LeaveRequest{Id=3,EmployeeId=8,Status="Pending"},new LeaveRequest{Id=4,EmployeeId=8,Status="Rejected"}}.AsQueryable();
        Check(personal.OwnLeaves(replies).Select(x=>x.Id).SequenceEqual(new[]{1,4}), "Leave replies include manager and final decisions without other employees or untouched applications");
        var responses=new[]{new QueryTicket{Id=1,EmployeeId=8,UpdatedAtUtc=DateTime.UtcNow},new QueryTicket{Id=2,EmployeeId=9,Status="Resolved"},new QueryTicket{Id=3,EmployeeId=8}}.AsQueryable();
        Check(personal.OwnQueries(responses).Select(x=>x.Id).SequenceEqual(new[]{1}), "Query responses restricted to submitting employee");
        var claimReplies=new[]{new ExpenseClaim{Id=1,EmployeeId=8,RecipientRespondedAtUtc=DateTime.UtcNow},new ExpenseClaim{Id=2,EmployeeId=9,RecipientRespondedAtUtc=DateTime.UtcNow},new ExpenseClaim{Id=3,EmployeeId=8}}.AsQueryable();
        Check(personal.OwnExpenses(claimReplies).Select(x=>x.Id).SequenceEqual(new[]{1}), "Final expense response reaches only its claimant");
        Check(personal.OwnAssets(new[]{new EmployeeAsset{EmployeeId=8},new EmployeeAsset{EmployeeId=9}}.AsQueryable()).Count()==1,"Asset allocations restricted to owner");
        Check(personal.OwnSalarySlips(new[]{new GeneratedSalarySlip{EmployeeId=8},new GeneratedSalarySlip{EmployeeId=9}}.AsQueryable()).Count()==1,"Salary notifications cannot expose another employee");
        Check(personal.OwnBankRequests(new[]{new BankDetailUpdateRequest{EmployeeId=8,ReviewedAtUtc=DateTime.UtcNow},new BankDetailUpdateRequest{EmployeeId=9,ReviewedAtUtc=DateTime.UtcNow},new BankDetailUpdateRequest{EmployeeId=8}}.AsQueryable()).Count()==1,"Bank decision notifications restricted to reviewed own requests");
        Check(personal.OwnAdvances(new[]{new ExpenseAdvance{EmployeeId=8},new ExpenseAdvance{EmployeeId=9}}.AsQueryable()).Count()==1,"Advance payment notification reaches only its owner");
        Check(personal.OwnTasks(db.WorkTasks).ToQueryString().Contains("AssigneeId"),"Personal assignments translate to PostgreSQL");
        Check(personal.OwnLeaves(db.LeaveRequests).ToQueryString().Contains("ManagerDecidedAtUtc"),"Personal leave decision scope translates to PostgreSQL");
        Check(personal.OwnExpenses(db.ExpenseClaims).ToQueryString().Contains("RecipientRespondedAtUtc"),"Personal expense response scope translates to PostgreSQL");
        Check(personal.OwnAssets(db.EmployeeAssets).ToQueryString().Contains("EmployeeId"),"Personal asset ownership translates to PostgreSQL");
        var legacy=new VertexERP.Controllers.MainController(null!,null!,null!,null!,null!,null!).EmployeeNotifications() as RedirectToActionResult;
        Check(legacy?.ActionName=="Index" && legacy.ControllerName=="Notifications", "Old employee notification URL opens the common inbox");
        await NotificationInboxViewChecks.Run();
        await NotificationRouteChecks.Run();
        Console.WriteLine($"{passed} notification checks and five role inbox renders passed.");
    }
}
