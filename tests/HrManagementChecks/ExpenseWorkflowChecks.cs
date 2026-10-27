using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using Vertex_ERP.Controllers;

internal static class ExpenseWorkflowChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        foreach (var managerDecision in new[] { "Approved", "Rejected" })
        {
            var claim = new ExpenseClaim { EmployeeId = 10, ReportingManagerId = 20 };
            Check(ExpenseClaimWorkflow.CanManagerReview(claim, 20), "Assigned manager may review " + managerDecision);
            Check(!ExpenseClaimWorkflow.CanManagerReview(claim, 21), "Other manager cannot review");
            Check(!ExpenseClaimWorkflow.CanHrReview(claim, 30), "HR cannot bypass manager stage");
            Check(!ExpenseClaimWorkflow.CanForward(claim, 30), "Unreviewed claim cannot be forwarded");
            ExpenseClaimWorkflow.ManagerReview(claim, managerDecision, "Manager note", 2);
            Check(claim.Status == "Pending" && claim.WorkflowStage == "HR" && claim.ManagerDecision == managerDecision, "Both manager decisions are routed to HR without losing the decision");
            Check(!ExpenseClaimWorkflow.CanManagerReview(claim, 20), "Manager cannot decide twice");
            Check(ExpenseClaimWorkflow.CanHrReview(claim, 30) && !ExpenseClaimWorkflow.CanHrReview(claim, 10), "HR review prevents self approval");
            ExpenseClaimWorkflow.HrReview(claim, "Approved", "HR note", 3);
            Check(claim.Status == "Approved" && claim.ManagerDecision == managerDecision && claim.DecisionNote == "HR note", "HR decision is stored separately from manager decision");
            Check(ExpenseClaimWorkflow.CanForward(claim, 30), "HR reviewed claim becomes forwardable");
            claim.WorkflowStage = "Forwarded"; claim.ForwardedToEmployeeId = 40;
            Check(!ExpenseClaimWorkflow.CanForward(claim, 30) && !ExpenseClaimWorkflow.CanHrReview(claim, 30), "Forwarded claims cannot be reviewed or forwarded twice");
        }
        foreach (var finalDecision in new[] { "Approved", "Rejected" })
        {
            var finalClaim = new ExpenseClaim { EmployeeId = 10, ReportingManagerId = 20, Amount = 750 };
            ExpenseClaimWorkflow.ManagerReview(finalClaim, "Approved", "Manager approved", 2);
            ExpenseClaimWorkflow.HrReview(finalClaim, "Approved", "HR approved", 3);
            finalClaim.WorkflowStage = "Forwarded"; finalClaim.ForwardedToEmployeeId = 40;
            Check(ExpenseClaimWorkflow.CanRecipientRespond(finalClaim, 40, true), "Assigned Yuvraj inbox can respond");
            Check(!ExpenseClaimWorkflow.CanRecipientRespond(finalClaim, 41, true) && !ExpenseClaimWorkflow.CanRecipientRespond(finalClaim, 40, false), "Other employees and revoked recipients cannot respond");
            Check(new ExpenseClaimPageViewModel { Claims = new[] { finalClaim } }.PendingAmount == finalClaim.Amount, "HR-approved claims still await final response");
            ExpenseClaimWorkflow.RecipientRespond(finalClaim, finalDecision, "Final response reason", 4);
            Check(finalClaim.WorkflowStage == "Completed" && finalClaim.Status == finalDecision && finalClaim.RecipientDecision == finalDecision, "Recipient decision finalizes claim as " + finalDecision);
            var finalSummary = new ExpenseClaimPageViewModel { Claims = new[] { finalClaim } };
            Check(finalSummary.PendingAmount == 0 && finalSummary.ApprovedAmount == (finalDecision == "Approved" ? 750 : 0)
                && finalSummary.RejectedAmount == (finalDecision == "Rejected" ? 750 : 0), "Expense totals use final Yuvraj decision");
            Check(finalClaim.HrDecision == "Approved" && finalClaim.ManagerDecision == "Approved" && finalClaim.DecisionNote == "HR approved", "Final response preserves earlier manager and HR decisions");
            Check(finalClaim.RecipientReason == "Final response reason" && finalClaim.RecipientRespondedByUserId == 4 && finalClaim.RecipientRespondedAtUtc.HasValue, "Response records reason and response identity/time");
            Check(!ExpenseClaimWorkflow.CanRecipientRespond(finalClaim, 40, true), "Final response cannot be submitted twice");
            Check(ExpenseClaimWorkflow.CanReadClaim(finalClaim, 10, false, false), "Original employee can read response and file");
            Check(!ExpenseClaimWorkflow.CanReadClaim(finalClaim, 99, false, false), "Unrelated employees cannot read response files");
            Check(ExpenseClaimWorkflow.CanReadClaim(finalClaim, 40, false, true) && ExpenseClaimWorkflow.CanReadClaim(finalClaim, 20, false, false), "Assigned recipient and manager retain response access");
        }
        bool ValidResponse(RespondToExpenseClaimViewModel value) => Validator.TryValidateObject(value, new ValidationContext(value), new List<ValidationResult>(), true);
        var reply = new RespondToExpenseClaimViewModel { Id = 1, Decision = "Approved", Reason = "Verified" };
        Check(!ValidResponse(reply), "Response attachment is mandatory");
        reply.ResponseFile = new FormFile(new MemoryStream("%PDF-1.7"u8.ToArray()), 0, 8, "ResponseFile", "response.pdf");
        Check(ValidResponse(reply), "Complete decision, reason and file pass validation");
        reply.Reason = " "; Check(!ValidResponse(reply), "Response reason is mandatory");
        reply.Reason = "Verified"; reply.Decision = "Paid"; Check(!ValidResponse(reply), "Invalid final decision is rejected");
        var selfClaim = new ExpenseClaim { EmployeeId = 1, ReportingManagerId = 1 };
        Check(!ExpenseClaimWorkflow.CanManagerReview(selfClaim, 1), "Manager cannot approve own claim");
        var rejected = new ExpenseClaim { EmployeeId = 1, WorkflowStage = "Reviewed", Status = "Rejected" };
        Check(ExpenseClaimWorkflow.CanForward(rejected, 2), "HR rejection can be forwarded while preserving rejected status");
        Check(!ExpenseClaimWorkflow.ValidDecision("Paid") && !ExpenseClaimWorkflow.ValidDecision(null), "Invalid decisions are rejected");
        foreach (var name in new[] { "Submit", "Decide", "Forward", "Respond" }) {
            var method = typeof(ExpenseController).GetMethod(name)!;
            Check(method.IsDefined(typeof(HttpPostAttribute), true) && method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), name + " requires POST and antiforgery");
        }
        Check(typeof(ExpenseController).GetMethod("Forward")!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(x => x.Roles == "HR,Admin"), "Only HR/Admin can forward");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        Check(db.Model.FindEntityType(typeof(ExpenseClaim))!.FindProperty(nameof(ExpenseClaim.WorkflowStage))!.IsConcurrencyToken, "Review stages protect against concurrent decisions");
        var sql = db.GetService<IMigrator>().GenerateScript("20261027130000_KeepOnlyYuvrajManualAttendance", "20261027140000_ExpenseManagerHrForwarding");
        Check(sql.Contains("JOIN \"Users\" account") && sql.Contains("yuvraj singh") && sql.Contains("HAVING count(*) = 1"), "Forward recipient is uniquely matched to an active Yuvraj login");
        Check(sql.Contains("WHEN \"Status\" <> 'Pending' THEN 'Reviewed'") && !sql.Contains("DELETE FROM"), "Migration preserves old decisions, files and claims");
        var responseSql = db.GetService<IMigrator>().GenerateScript("20261027140000_ExpenseManagerHrForwarding", "20261027150000_ExpenseRecipientResponse");
        Check(responseSql.Contains("ResponseStoredFileName") && responseSql.Contains("RecipientReason") && responseSql.Contains("REFERENCES \"Users\""), "Response migration stores attachment metadata and uses mapped user table");
        Check(responseSql.Contains("SET \"HrDecision\" = \"Status\"") && !responseSql.Contains("DELETE FROM"), "Response migration preserves previous HR decisions");
        await ExpenseViewChecks.Run();
        Console.WriteLine($"{passed} expense workflow checks and seven route view renders passed.");
    }
}
