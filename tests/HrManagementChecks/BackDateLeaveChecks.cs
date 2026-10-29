using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class BackDateLeaveChecks
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        var past = new DateOnly(2025, 12, 31);
        Check(LeaveApprovalWorkflow.ValidApplication("CL", past, past, "Past leave", false), "Back-date application allowed");
        Check(!LeaveApprovalWorkflow.ValidApplication("CL", default, past, "Reason", false), "Missing start rejected");
        Check(!LeaveApprovalWorkflow.ValidApplication("CL", past, past.AddDays(-1), "Reason", false), "Reversed range rejected");
        Check(!LeaveApprovalWorkflow.ValidApplication("CL", past, past.AddDays(1), "Reason", true), "Multiple-day half leave rejected");
        Check(!LeaveApprovalWorkflow.ValidApplication("CL", past, past, " ", false), "Blank reason rejected");
        var leave = new LeaveRequest { EmployeeId = 1, LeaveType = "CL", FromDate = past, ToDate = past.AddDays(1) };
        Check(!LeaveApprovalWorkflow.DecideFinal(leave, "Approved", 3, null), "HR cannot bypass manager");
        Check(LeaveApprovalWorkflow.DecideManager(leave, "Approved", 2, "Manager", "Reviewed"), "Manager can approve");
        Check(leave.Status == "Pending" && leave.ApprovalLevel == "HR/Admin", "Manager approval stays pending final decision");
        Check(LeaveBalanceCalculator.Days(new[] { leave }, 2025, "Approved") == 0, "Manager approval does not deduct leave");
        Check(!LeaveApprovalWorkflow.DecideManager(leave, "Rejected", 2, "Manager", null), "Manager cannot decide twice");
        Check(!LeaveApprovalWorkflow.DecideFinal(leave, "invalid", 3, null), "Invalid final decision rejected");
        Check(!LeaveApprovalWorkflow.DecideFinal(leave, "Approved", 3, new string('x', 501)), "Oversized remarks rejected");
        Check(LeaveApprovalWorkflow.DecideFinal(leave, "Approved", 3, "Confirmed"), "Final approval succeeds");
        Check(leave.ManagerDecisionNote == "Reviewed" && leave.ManagerDecidedByUserId == 2 && leave.DecidedByUserId == 3 && leave.DecisionNote == "Confirmed", "Both decisions retained");
        Check(LeaveBalanceCalculator.Days(new[] { leave }, 2025, "Approved") == 1 && LeaveBalanceCalculator.Days(new[] { leave }, 2026, "Approved") == 1, "Back-date deductions use correct year");
        Check(!LeaveApprovalWorkflow.DecideFinal(leave, "Rejected", 3, null), "Final decision cannot be repeated");
        var rejected = new LeaveRequest();
        Check(LeaveApprovalWorkflow.DecideManager(rejected, "Rejected", 2, "Manager", "Declined") && rejected.Status == "Pending" && rejected.ApprovalLevel == "HR/Admin", "Manager rejection forwards to HR without finalizing");
        Check(LeaveApprovalWorkflow.DecideFinal(rejected, "Rejected", 3, "Final rejection") && rejected.ManagerDecision == "Rejected", "HR finalizes rejection while preserving manager review");
        var bypass = new LeaveRequest { ApprovalLevel="HR/Admin" };
        Check(!LeaveApprovalWorkflow.DecideFinal(bypass,"Approved",3,null),"Legacy direct HR stage cannot bypass a missing manager decision");
        Check(!LeaveApprovalWorkflow.CanTakeFinalAction(new LeaveRequest(),false,true),"Admin is view only for regular leave");
        var applicant=new Employee{Id=1,ReportingManagerId=2};
        foreach(var type in new[]{"EL","CL","ML","EW"}) {
            var routed=new LeaveRequest{EmployeeId=1,LeaveType=type,FromDate=past,ToDate=past};
            LeaveApprovalWorkflow.RouteToManager(routed,applicant);
            Check(routed.ApprovalLevel=="Manager"&&routed.AssignedApproverEmployeeId==2,type+" always routes to reporting manager");
            EmployeeRequestChanges.UpdateLeave(routed,applicant,true,type,past,past,"Updated by senior employee",false);
            Check(routed.ApprovalLevel=="Manager"&&routed.AssignedApproverEmployeeId==2,"Senior edit cannot bypass manager for "+type);
        }
        Check(!LeaveApprovalWorkflow.CanRouteToManager(new Employee{Id=1}),"Missing reporting manager blocks applications");
        Check(!LeaveApprovalWorkflow.CanRouteToManager(new Employee{Id=1,ReportingManagerId=1}),"Employee cannot be own approver");
        var hrRejected = new LeaveRequest();
        LeaveApprovalWorkflow.DecideManager(hrRejected, "Approved", 2, "Manager", null);
        Check(LeaveApprovalWorkflow.DecideFinal(hrRejected, "Rejected", 3, "Declined") && hrRejected.ManagerDecision == "Approved" && hrRejected.Status == "Rejected", "HR rejection preserves manager approval");
        Check(LeaveApprovalWorkflow.ValidHalfDaySession(true, "First") && LeaveApprovalWorkflow.ValidHalfDaySession(true, "Second"), "Both half-day sessions accepted");
        Check(!LeaveApprovalWorkflow.ValidHalfDaySession(true, null) && !LeaveApprovalWorkflow.ValidHalfDaySession(true, "invalid"), "Half-day session required and validated");
        foreach (var managerDecision in new[] { "Approved", "Rejected" })
        foreach (var finalDecision in new[] { "Approved", "Rejected" })
        {
            var half = new LeaveRequest { EmployeeId = 1, IsHalfDay = true, HalfDaySession = "Second", LeaveType = "CL", FromDate = past, ToDate = past };
            Check(!LeaveApprovalWorkflow.DecideFinal(half, finalDecision, 3, null), "Half day cannot bypass manager");
            Check(LeaveApprovalWorkflow.DecideManager(half, managerDecision, 2, "Manager", "Reviewed") && half.Status == "Pending" && half.ApprovalLevel == "HR/Admin", "Half-day manager " + managerDecision + " forwards to HR");
            Check(LeaveBalanceCalculator.Days(new[] { half }, 2025, "Approved") == 0, "Manager decision does not deduct half day");
            Check(LeaveApprovalWorkflow.DecideFinal(half, finalDecision, 3, "Final review") && half.ManagerDecision == managerDecision && half.HalfDaySession == "Second", "HR final decision preserves manager decision and session");
            Check(LeaveBalanceCalculator.Days(new[] { half }, 2025, "Approved") == (finalDecision == "Approved" ? .5m : 0m), "Only HR approval deducts 0.5 day");
        }
        foreach(var action in new[]{"DecideManagerLeave","CancelApprovedLeave"})
            Check(typeof(VertexERP.Controllers.MainController).GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles=="HR",action+" is restricted to HR at the HTTP boundary");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options);
        var sql=db.GetService<IMigrator>().GenerateScript();
        Check(sql.Contains("UPDATE \"LeaveRequests\" AS r") && sql.Contains("r.\"Status\" = 'Pending'") && sql.Contains("r.\"ManagerDecision\" IS NULL"),"Pending unreviewed legacy requests are rerouted without reopening completed decisions");
        Console.WriteLine($"{passed} back-date leave checks passed.");
    }
}
