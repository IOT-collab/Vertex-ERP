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
        Check(LeaveApprovalWorkflow.DecideManager(rejected, "Rejected", 2, "Manager", "Declined") && rejected.Status == "Rejected", "Manager rejection is final");
        Check(!LeaveApprovalWorkflow.DecideFinal(rejected, "Approved", 3, null), "HR cannot approve manager-rejected request");
        var hrRejected = new LeaveRequest();
        LeaveApprovalWorkflow.DecideManager(hrRejected, "Approved", 2, "Manager", null);
        Check(LeaveApprovalWorkflow.DecideFinal(hrRejected, "Rejected", 3, "Declined") && hrRejected.ManagerDecision == "Approved" && hrRejected.Status == "Rejected", "HR rejection preserves manager approval");
        Console.WriteLine($"{passed} back-date leave checks passed.");
    }
}
