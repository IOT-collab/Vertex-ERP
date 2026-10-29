using VertexERP.Models;

namespace VertexERP.Services;

public static class EmployeeRequestChanges
{
    public static bool CanChangeLeave(LeaveRequest request, int employeeId) => request.EmployeeId == employeeId && request.Status == "Pending" && !request.IsHrSubmitted;
    public static bool CanEditQuery(QueryTicket ticket, int employeeId) => ticket.EmployeeId == employeeId && ticket.Status == "Open" && string.IsNullOrEmpty(ticket.Resolution);

    public static void UpdateLeave(LeaveRequest request, Employee employee, bool senior, string type, DateOnly from, DateOnly to,
        string reason, bool halfDay, string? halfDaySession = null, TimeOnly? shortLeaveFrom = null, TimeOnly? shortLeaveTo = null)
    {
        type = halfDay ? ExtraLeaveFacility.HalfDay : type.Trim();
        var facility = ExtraLeaveFacility.IsFacilityType(type);
        // Editing never bypasses the reporting manager, regardless of the applicant role.
        var valid = facility ? ExtraLeaveFacility.Valid(type, from, to, reason, halfDaySession, shortLeaveFrom, shortLeaveTo)
            : LeaveApprovalWorkflow.ValidApplication(type, from, to, reason, false);
        if (!CanChangeLeave(request, employee.Id) || !valid || !LeaveApprovalWorkflow.CanRouteToManager(employee))
            throw new InvalidOperationException("Leave cannot be edited.");
        request.LeaveType = facility ? type : type.ToUpperInvariant();
        request.FromDate = from; request.ToDate = to; request.Reason = reason.Trim();
        ExtraLeaveFacility.SetDetails(request, request.LeaveType, halfDaySession, shortLeaveFrom, shortLeaveTo);
        LeaveApprovalWorkflow.RouteToManager(request, employee);
        request.ManagerDecision = null; request.ManagerDecidedByUserId = null;
        request.ManagerDecidedByName = null; request.ManagerDecidedAtUtc = null; request.ManagerDecisionNote = null;
        request.DecidedByUserId = null; request.DecidedAtUtc = null; request.DecisionNote = null;
        request.AppliedAtUtc = DateTime.UtcNow;
    }
}
