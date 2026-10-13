using VertexERP.Models;

namespace VertexERP.Services;

public static class LeaveApprovalWorkflow
{
    public static bool ValidApplication(string? type, DateOnly from, DateOnly to, string? reason, bool halfDay) =>
        LeaveBalanceCalculator.Categories.Contains(type?.Trim().ToUpperInvariant())
        && from != default && to >= from && (!halfDay || from == to)
        && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    public static bool DecideManager(LeaveRequest request, string? decision, int userId, string name, string? note)
    {
        if (request.Status != "Pending" || request.ApprovalLevel != "Manager" || !ValidDecision(decision, note)) return false;
        request.ManagerDecision = Normalize(decision!);
        request.ManagerDecidedByUserId = userId;
        request.ManagerDecidedByName = name;
        request.ManagerDecidedAtUtc = DateTime.UtcNow;
        request.ManagerDecisionNote = note?.Trim();
        if (request.ManagerDecision == "Approved") request.ApprovalLevel = "HR/Admin";
        else SetFinal(request, "Rejected", userId, note);
        return true;
    }

    public static bool DecideFinal(LeaveRequest request, string? decision, int userId, string? note)
    {
        if (request.Status != "Pending" || request.ApprovalLevel != "HR/Admin" || !ValidDecision(decision, note)) return false;
        SetFinal(request, Normalize(decision!), userId, note);
        return true;
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
