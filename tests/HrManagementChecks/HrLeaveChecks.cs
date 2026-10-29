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

internal static class HrLeaveChecks
{
    public static async Task Run()
    {
        var passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        var date=new DateOnly(2026,1,10);
        var form=new HrLeaveApplication{EmployeeId=8,FromDate=date,ToDate=date.AddDays(1),Reason="Employee informed HR",LeaveType="EL"};
        Check(HrLeaveApplications.Valid(form),"Past-date regular leave accepted");
        form.EmployeeId=0;Check(!HrLeaveApplications.Valid(form),"Employee selection required");form.EmployeeId=8;
        form.Reason=new string('x',501);Check(!HrLeaveApplications.Valid(form),"Reason length bounded");form.Reason="Informed HR";
        form.ToDate=date.AddDays(366);Check(!HrLeaveApplications.Valid(form),"Oversized date span rejected");form.ToDate=date.AddDays(1);
        form.LeaveType="Half Day";form.ToDate=date;form.HalfDaySession="First";Check(HrLeaveApplications.Valid(form),"First half accepted");
        form.HalfDaySession="Second";Check(HrLeaveApplications.Valid(form),"Second half accepted");form.HalfDaySession="invalid";Check(!HrLeaveApplications.Valid(form),"Invalid half rejected");
        form.LeaveType="Short Leave";form.ShortLeaveFrom=new TimeOnly(11,0);form.ShortLeaveTo=new TimeOnly(13,0);Check(HrLeaveApplications.Valid(form),"Two-hour short leave accepted");form.ShortLeaveTo=new TimeOnly(13,1);Check(!HrLeaveApplications.Valid(form),"Over two-hour short leave rejected");
        var candidate=new LeaveRequest{EmployeeId=8,LeaveType="EL",FromDate=date,ToDate=date.AddDays(1),SubmittedByHrUserId=2,SubmittedByHrName="HR",AssignedApproverEmployeeId=7};
        var balance=new ManualLeaveBalance{EmployeeId=8,Year=2026,Category="EL",TotalDays=10,UsedDays=1};
        var previous=new[]{new LeaveRequest{Id=1,EmployeeId=8,LeaveType="EL",FromDate=new DateOnly(2026,1,1),ToDate=new DateOnly(2026,1,2),Status="Approved"},new LeaveRequest{Id=2,EmployeeId=8,LeaveType="EL",FromDate=new DateOnly(2026,1,4),ToDate=new DateOnly(2026,1,5),Status="Pending"}};
        Check(HrLeaveApplications.Error(candidate,previous,new[]{balance})==null,"Regular balance accounts for additional used, approved and pending leave");
        balance.TotalDays=5;Check(HrLeaveApplications.Error(candidate,previous,new[]{balance})!.Contains("Insufficient"),"Pending applications reserve availability");balance.TotalDays=10;
        Check(HrLeaveApplications.Error(candidate,[],[])!.Contains("calculate"),"Missing balance rejected with clear message");
        var duplicate=new LeaveRequest{Id=3,EmployeeId=8,LeaveType="ML",FromDate=date,ToDate=date,Status="Approved"};
        Check(HrLeaveApplications.Error(candidate,new[]{duplicate},new[]{balance})!.Contains("already covers"),"Overlapping dates blocked across categories");duplicate.Status="Rejected";Check(HrLeaveApplications.Error(candidate,new[]{duplicate},new[]{balance})==null,"Rejected request does not block reapplication");
        var free=new LeaveRequest{EmployeeId=8,LeaveType=ExtraLeaveFacility.HalfDay,IsExtraFacility=true,IsHalfDay=true,HalfDaySession="First",FromDate=date,ToDate=date};
        Check(HrLeaveApplications.Error(free,[],[])==null,"Extra facility needs no regular balance");
        var halves=new[]{new LeaveRequest{Id=5,EmployeeId=8,LeaveType=ExtraLeaveFacility.HalfDay,IsExtraFacility=true,IsHalfDay=true,HalfDaySession="First",FromDate=date.AddDays(-3),ToDate=date.AddDays(-3)},new LeaveRequest{Id=6,EmployeeId=8,LeaveType=ExtraLeaveFacility.HalfDay,IsExtraFacility=true,IsHalfDay=true,HalfDaySession="Second",FromDate=date.AddDays(-2),ToDate=date.AddDays(-2)}};
        Check(HrLeaveApplications.Error(free,halves,[])!.Contains("monthly"),"HR cannot exceed monthly facility quota");
        Check(!LeaveApprovalWorkflow.DecideFinal(candidate,"Approved",2,null),"HR cannot bypass manager");
        Check(LeaveApprovalWorkflow.DecideManager(candidate,"Rejected",4,"Manager","Needs HR review") && candidate.Status=="Pending" && candidate.ApprovalLevel=="HR/Admin", "Manager rejection of HR-submitted leave proceeds to HR final review");
        Check(!LeaveApprovalWorkflow.CanTakeFinalAction(candidate,false,true),"Admin cannot decide HR-submitted leave");
        Check(LeaveApprovalWorkflow.CanTakeFinalAction(candidate,true,false),"HR can make final decision");
        Check(!EmployeeRequestChanges.CanChangeLeave(candidate,8),"Employee cannot rewrite HR-submitted application");
        var employeeScope=new NotificationInboxScope("Employee",8);
        var initial=new LeaveRequest{EmployeeId=8,SubmittedByHrUserId=2};
        Check(employeeScope.OwnLeaves(new[]{initial,new LeaveRequest{EmployeeId=9,SubmittedByHrUserId=2}}.AsQueryable()).Count()==1,"Employee notified as soon as HR applies on their behalf");
        LeaveBalanceCalculator.ApplyRequests(new[]{balance},new[]{candidate});Check(balance.ApprovedDays==0,"Manager review does not deduct regular balance");
        Check(LeaveApprovalWorkflow.DecideFinal(candidate,"Approved",2,"Confirmed") && candidate.ManagerDecision=="Rejected", "HR final decision preserves manager decision and original HR source");
        LeaveBalanceCalculator.ApplyRequests(new[]{balance},new[]{candidate});Check(balance.ApprovedDays==2,"Final approval deducts exact requested days");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options);
        var controller=new HrLeaveController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        Check(await controller.Apply(form) is ForbidResult,"Unidentified actor cannot apply on behalf");
        controller.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"Admin"),new Claim(ClaimTypes.NameIdentifier,"1")},"test"));
        Check(await controller.Apply(form) is ForbidResult,"Admin POST denied before database access");
        var apply=typeof(HrLeaveController).GetMethod("Apply")!;
        Check(apply.IsDefined(typeof(HttpPostAttribute),true)&&apply.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),"HR submission requires POST and antiforgery");
        Check(apply.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles=="HR","Only HR can submit");
        var script=db.GetService<IMigrator>().GenerateScript();
        Check(script.Contains("SubmittedByHrUserId")&&script.Contains("FK_LeaveRequests_Users_SubmittedByHrUserId"),"HR source migration links to correct Users table");
        await HrLeaveViewChecks.Run();
        Console.WriteLine($"{passed} HR leave checks and two role renders passed.");
    }
}
