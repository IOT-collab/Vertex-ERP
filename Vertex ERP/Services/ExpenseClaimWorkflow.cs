using VertexERP.Models;

namespace VertexERP.Services;

public static class ExpenseClaimWorkflow
{
    public const string Manager = "Manager", HR = "HR", Reviewed = "Reviewed", Forwarded = "Forwarded", Completed = "Completed";
    public static bool CanManagerReview(ExpenseClaim claim, int? employeeId) => employeeId.HasValue
        && claim.WorkflowStage == Manager && claim.Status == "Pending"
        && claim.ReportingManagerId == employeeId && claim.EmployeeId != employeeId;
    public static bool CanHrReview(ExpenseClaim claim, int? employeeId) =>
        claim.WorkflowStage == HR && claim.Status == "Pending" && claim.EmployeeId != employeeId;
    public static bool CanForward(ExpenseClaim claim, int? employeeId) =>
        claim.WorkflowStage == Reviewed && claim.Status is "Approved" or "Rejected" && claim.EmployeeId != employeeId;
    public static bool CanReadClaim(ExpenseClaim claim, int? employeeId, bool isHrOrAdmin, bool hasInboxAccess) =>
        isHrOrAdmin || (employeeId.HasValue && (claim.EmployeeId == employeeId || claim.ReportingManagerId == employeeId
            || (hasInboxAccess && claim.ForwardedToEmployeeId == employeeId && claim.WorkflowStage is Forwarded or Completed)));
    public static bool CanRecipientRespond(ExpenseClaim claim, int? employeeId, bool hasInboxAccess) =>
        hasInboxAccess && employeeId.HasValue && claim.WorkflowStage == Forwarded
        && claim.ForwardedToEmployeeId == employeeId && claim.EmployeeId != employeeId;
    public static void RecipientRespond(ExpenseClaim claim, string decision, string reason, int actorId)
    {
        claim.HrDecision ??= claim.Status;
        claim.RecipientDecision = decision; claim.RecipientReason = reason;
        claim.RecipientRespondedByUserId = actorId; claim.RecipientRespondedAtUtc = DateTime.UtcNow;
        claim.Status = decision; claim.WorkflowStage = Completed;
    }
    public static bool ValidDecision(string? value) => value is "Approved" or "Rejected";
    public static string StageLabel(ExpenseClaim claim) => claim.WorkflowStage switch {
        Manager => "Awaiting manager", HR => "Awaiting HR", Reviewed => "HR reviewed · Ready to forward",
        Forwarded => "Awaiting Yuvraj response", Completed => "Final response received", _ => "Reviewed"
    };
    public static void ManagerReview(ExpenseClaim claim, string decision, string? note, int actorId)
    {
        claim.ManagerDecision = decision; claim.ManagerNote = note;
        claim.ManagerDecidedByUserId = actorId; claim.ManagerDecidedAtUtc = DateTime.UtcNow;
        claim.RequiresHrApproval = true; claim.WorkflowStage = HR;
    }
    public static void HrReview(ExpenseClaim claim, string decision, string? note, int actorId)
    {
        claim.HrDecision = decision; claim.Status = decision; claim.DecisionNote = note; claim.DecidedByUserId = actorId;
        claim.DecidedAtUtc = DateTime.UtcNow; claim.WorkflowStage = Reviewed;
    }
}
