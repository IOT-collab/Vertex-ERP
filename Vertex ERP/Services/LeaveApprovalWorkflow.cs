using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class LeaveApprovalWorkflow
{
    public static bool ValidApplication(string? type, DateOnly from, DateOnly to, string? reason, bool halfDay) =>
        LeaveBalanceCalculator.Categories.Contains(type?.Trim().ToUpperInvariant())
        && from != default && to >= from && (!halfDay || from == to)
        && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    public static bool ValidHalfDaySession(bool halfDay, string? session) => !halfDay || session is "First" or "Second";

    public static bool DecideManager(LeaveRequest request, string? decision, int userId, string name, string? note)
    {
        if (request.Status != "Pending" || request.ApprovalLevel != "Manager" || !ValidDecision(decision, note)) return false;
        request.ManagerDecision = Normalize(decision!);
        request.ManagerDecidedByUserId = userId;
        request.ManagerDecidedByName = name;
        request.ManagerDecidedAtUtc = DateTime.UtcNow;
        request.ManagerDecisionNote = note?.Trim();
        request.ApprovalLevel = "HR/Admin";
        return true;
    }

    public static bool CanTakeFinalAction(LeaveRequest request, bool isHr, bool isAdmin) =>
        isHr;

    public static bool DecideFinal(LeaveRequest request, string? decision, int userId, string? note)
    {
        if (request.Status != "Pending" || request.ApprovalLevel != "HR/Admin" || !HasManagerReview(request) || !ValidDecision(decision, note)) return false;
        SetFinal(request, Normalize(decision!), userId, note);
        return true;
    }

    public static bool HasManagerReview(LeaveRequest request) => request.ManagerDecision is "Approved" or "Rejected" && request.ManagerDecidedAtUtc.HasValue;

    public static bool CanRouteToManager(Employee employee) => employee.IsActive && employee.ReportingManagerId.HasValue && employee.ReportingManagerId!=employee.Id;

    public static async Task<bool> ReportingManagerReadyAsync(ApplicationDbContext db, Employee employee)
    {
        if(!CanRouteToManager(employee)) return false;
        var roles=await db.AppUsers.AsNoTracking().Where(x=>x.EmployeeId==employee.ReportingManagerId && x.IsActive && x.Employee!=null && x.Employee.IsActive).Select(x=>x.Role).ToListAsync();
        return roles.Any(role=>AccountRoleService.EffectiveRole(role,true)==AccountRoleService.Manager);
    }

    public static void RouteToManager(LeaveRequest request,Employee employee)
    {
        if(request.Status!="Pending" || request.EmployeeId!=employee.Id || !CanRouteToManager(employee)) throw new InvalidOperationException("An active reporting manager must review this application.");
        request.ApprovalLevel="Manager";request.AssignedApproverEmployeeId=employee.ReportingManagerId;
    }

    private static string Normalize(string decision) => decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Rejected";
    private static bool ValidDecision(string? decision, string? note) =>
        (string.Equals(decision, "Approved", StringComparison.OrdinalIgnoreCase) || string.Equals(decision, "Rejected", StringComparison.OrdinalIgnoreCase)) && (note?.Trim().Length ?? 0) <= 500;

    private static void SetFinal(LeaveRequest request, string decision, int userId, string? note)
    {
        request.Status = decision;
        request.DecidedByUserId = userId;
        request.DecidedAtUtc = DateTime.UtcNow;
        request.DecisionNote = note?.Trim();
    }
}
