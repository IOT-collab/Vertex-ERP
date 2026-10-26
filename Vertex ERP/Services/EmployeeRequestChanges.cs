using VertexERP.Models;

namespace VertexERP.Services;

public static class EmployeeRequestChanges
{
    public static bool CanChangeLeave(LeaveRequest request, int employeeId) => request.EmployeeId == employeeId && request.Status == "Pending";
    public static bool CanEditQuery(QueryTicket ticket, int employeeId) => ticket.EmployeeId == employeeId && ticket.Status == "Open" && string.IsNullOrEmpty(ticket.Resolution);

    public static void UpdateLeave(LeaveRequest request, Employee employee, bool senior, string type, DateOnly from, DateOnly to, string reason, bool halfDay)
    {
        if (!CanChangeLeave(request, employee.Id) || !LeaveApprovalWorkflow.ValidApplication(type, from, to, reason, halfDay)
            || (!senior && !employee.ReportingManagerId.HasValue)) throw new InvalidOperationException("Leave cannot be edited.");
        request.LeaveType = type.Trim().ToUpperInvariant();
        request.FromDate = from; request.ToDate = to; request.Reason = reason.Trim(); request.IsHalfDay = halfDay;
        request.ApprovalLevel = senior ? "HR/Admin" : "Manager";
        request.AssignedApproverEmployeeId = senior ? null : employee.ReportingManagerId;
        request.ManagerDecision = null; request.ManagerDecidedByUserId = null;
        request.ManagerDecidedByName = null; request.ManagerDecidedAtUtc = null; request.ManagerDecisionNote = null;
        request.DecidedByUserId = null; request.DecidedAtUtc = null; request.DecisionNote = null;
    }
}
