using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

// Activity is read from the source record so failed submissions never produce a notification.
// Per-user event receipts keep personal and management read state independent for every login.
public sealed class NotificationInboxService(ApplicationDbContext db)
{
    public async Task<int?> ViewerIdAsync(ClaimsPrincipal principal, CancellationToken token = default)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.AppUsers.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(token);
    }

    public async Task<IReadOnlyList<InboxNotification>> LoadAsync(ClaimsPrincipal principal, CancellationToken token = default)
    {
        var userId = await ViewerIdAsync(principal, token);
        if (!userId.HasValue) return [];
        var viewer = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == userId, token);
        var effectiveRole = await AccountRoleService.ResolveAsync(db, viewer) ?? "";
        var scope = new NotificationInboxScope(effectiveRole, viewer.EmployeeId);
        var management = scope.Management;
        var manager = scope.Manager;
        if (!management && !manager && !scope.Personal) return [];
        var employeeId = viewer.EmployeeId;
        if (!management && (!employeeId.HasValue || !await db.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive, token))) return [];
        var items = new List<InboxNotification>();
        void Add(string type, int id, string category, string title, string detail, string url, DateTime at, string version = "")
            => items.Add(new($"{type}:{id}:{at.Ticks}:{version}", category, title, detail, url, at));

        if (management || manager)
        {
            var leaves = await scope.Leaves(db.LeaveRequests.AsNoTracking())
                .Select(x => new { x.Id, x.Employee.FullName, x.LeaveType, x.Status, x.ApprovalLevel, x.AppliedAtUtc, x.ManagerDecidedAtUtc, x.DecidedAtUtc }).ToListAsync(token);
            foreach (var x in leaves) Add("Leave", x.Id, "Leave", x.FullName + " · " + x.LeaveType,
                x.Status + " · " + (x.ApprovalLevel=="HR/Admin"?"HR":x.ApprovalLevel), manager ? "/Main/ManagerLeaves" : x.Status == "Pending" ? "/Main/WorkflowManagement" : "/Main/LeaveStatus", x.DecidedAtUtc ?? x.ManagerDecidedAtUtc ?? x.AppliedAtUtc, x.Status + x.ApprovalLevel);

            var queries = await scope.Queries(db.QueryTickets.AsNoTracking())
                .Select(x => new { x.Id, x.Employee.FullName, x.Subject, x.Status, x.CreatedAtUtc, x.UpdatedAtUtc }).ToListAsync(token);
            foreach (var x in queries) Add("Query", x.Id, "Queries", x.FullName + " · " + x.Subject, "Query · " + x.Status,
                x.Status == "Resolved" || x.Status == "Closed" ? "/Main/ClosedIssues" : manager ? "/Main/ManagerQueries" : "/Main/WorkflowManagement", x.UpdatedAtUtc ?? x.CreatedAtUtc, x.Status);

            // HR's expense screen deliberately excludes the manager stage; notifications follow that same scope.
            var expenses = await scope.Expenses(db.ExpenseClaims.AsNoTracking())
                .Select(x => new { x.Id, x.Employee.FullName, x.Title, x.WorkflowStage, x.Status, x.SubmittedAtUtc, x.ManagerDecidedAtUtc, x.DecidedAtUtc, x.ForwardedAtUtc, x.RecipientRespondedAtUtc }).ToListAsync(token);
            foreach (var x in expenses) Add("Expense", x.Id, "Expenses", x.FullName + " · " + x.Title, "Expense · " + x.WorkflowStage + " · " + x.Status,
                "/Expense/Index", x.RecipientRespondedAtUtc ?? x.ForwardedAtUtc ?? x.DecidedAtUtc ?? x.ManagerDecidedAtUtc ?? x.SubmittedAtUtc, x.WorkflowStage + x.Status);

            var tasks = await scope.Reports(db.WorkTasks.AsNoTracking())
                .Select(x => new { x.Id, x.Assignee.FullName, x.Title, x.Status, x.UpdatedAtUtc }).ToListAsync(token);
            foreach (var x in tasks) Add("Task", x.Id, "Reports", x.FullName + " · " + x.Title, "Task update · " + x.Status, "/Main/TaskMgm", x.UpdatedAtUtc!.Value, x.Status);

        }

        if (management)
        {
            var feedback = await db.EmployeeFeedbacks.AsNoTracking().Select(x => new { x.Id, x.SenderName, x.Subject, x.SubmittedAtUtc }).ToListAsync(token);
            foreach (var x in feedback) Add("Feedback", x.Id, "Feedback", x.SenderName + " · " + x.Subject, "Written feedback received", "/Feedback/MessageInbox?search=" + Uri.EscapeDataString(x.Subject), x.SubmittedAtUtc);
            var submissions = await db.FeedbackDocumentSubmissions.AsNoTracking().Select(x => new { x.Id, x.Employee.FullName, x.Document.Title, x.DocumentId, x.Revision, x.SubmittedAtUtc }).ToListAsync(token);
            foreach (var x in submissions) Add("Form", x.Id, "Feedback", x.FullName + " · " + x.Title, "Form response uploaded · Version " + x.Revision,
                "/FeedbackDocuments/Details/" + x.DocumentId + "?search=" + Uri.EscapeDataString(x.FullName) + "&status=submitted", x.SubmittedAtUtc, x.Revision.ToString());
            var assets = await db.EmployeeAssets.AsNoTracking().Where(x => x.RespondedAtUtc != null).Select(x => new { x.Id, x.Employee.FullName, x.AssetName, x.Status, x.RespondedAtUtc }).ToListAsync(token);
            foreach (var x in assets) Add("Asset", x.Id, "Assets", x.FullName + " · " + x.AssetName, "Asset response · " + x.Status, "/Hr/AssetManagement", x.RespondedAtUtc!.Value, x.Status);
            var documents = await db.EmployeeDocuments.AsNoTracking().Select(x => new { x.Id, x.EmployeeId, x.Employee.FullName, x.DocumentName, x.UploadedAtUtc }).ToListAsync(token);
            foreach (var x in documents) Add("Document", x.Id, "Documents", x.FullName + " · " + x.DocumentName, "Employee document uploaded", "/Hr/EmpDocuments?employeeId=" + x.EmployeeId, x.UploadedAtUtc);
            var banks = await db.BankDetailUpdateRequests.AsNoTracking().Where(x => scope.BankRequests).Select(x => new { x.Id, x.Employee.FullName, x.RequestedAtUtc, x.Status }).ToListAsync(token);
            foreach (var x in banks) Add("Bank", x.Id, "Bank Requests", x.FullName + " · Bank details update", x.Status, "/Main/BankUpdateRequests", x.RequestedAtUtc, x.Status);
        }
        if (scope.Personal && await db.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive, token))
        {
            var ownTasks = await scope.OwnTasks(db.WorkTasks.AsNoTracking()).Select(x => new { x.Id, x.Title, x.Status, x.CreatedAtUtc, x.UpdatedAtUtc }).ToListAsync(token);
            foreach (var x in ownTasks) Add("OwnTask", x.Id, "Tasks", "Your task · " + x.Title, "Assigned to you · " + x.Status,
                effectiveRole == AccountRoleService.Employee ? "/Main/EmployeeTasks" : "/Main/Manager", x.UpdatedAtUtc ?? x.CreatedAtUtc, x.Status);
            var ownLeaves = await scope.OwnLeaves(db.LeaveRequests.AsNoTracking()).Select(x => new { x.Id, x.LeaveType, x.Status, x.ApprovalLevel, x.ManagerDecision, x.ManagerDecidedAtUtc, x.DecidedAtUtc, x.AppliedAtUtc, x.SubmittedByHrUserId }).ToListAsync(token);
            foreach (var x in ownLeaves) Add("OwnLeave", x.Id, "Leave", "Your " + x.LeaveType + " request updated",
                x.Status == "Pending" ? (x.ManagerDecision == null && x.SubmittedByHrUserId.HasValue ? "HR applied on your behalf" : "Manager: " + x.ManagerDecision) + " · Awaiting " + (x.ApprovalLevel == "HR/Admin" ? "HR" : x.ApprovalLevel) : x.Status,
                "/Main/EmployeeLeaves", x.DecidedAtUtc ?? x.ManagerDecidedAtUtc ?? x.AppliedAtUtc, x.Status + x.ApprovalLevel + x.ManagerDecision);
            var ownQueries = await scope.OwnQueries(db.QueryTickets.AsNoTracking()).Select(x => new { x.Id, x.Subject, x.Status, x.Resolution, x.CreatedAtUtc, x.UpdatedAtUtc }).ToListAsync(token);
            foreach (var x in ownQueries) Add("OwnQuery", x.Id, "Queries", "Response to your query · " + x.Subject,
                x.Status + (string.IsNullOrWhiteSpace(x.Resolution) ? "" : " · " + x.Resolution), "/Main/EmployeeQueries", x.UpdatedAtUtc ?? x.CreatedAtUtc, x.Status);
            var ownExpenses = await scope.OwnExpenses(db.ExpenseClaims.AsNoTracking()).Select(x => new { x.Id, x.Title, x.Status, x.WorkflowStage, x.ManagerDecision, x.HrDecision, x.RecipientDecision, x.SubmittedAtUtc, x.ManagerDecidedAtUtc, x.DecidedAtUtc, x.ForwardedAtUtc, x.RecipientRespondedAtUtc }).ToListAsync(token);
            foreach (var x in ownExpenses) Add("OwnExpense", x.Id, "Expenses", "Your expense claim · " + x.Title,
                "Stage: " + x.WorkflowStage + " · " + (x.RecipientDecision ?? x.HrDecision ?? x.ManagerDecision ?? x.Status), "/Expense/Index?view=self",
                x.RecipientRespondedAtUtc ?? x.ForwardedAtUtc ?? x.DecidedAtUtc ?? x.ManagerDecidedAtUtc ?? x.SubmittedAtUtc, x.WorkflowStage + x.Status + x.RecipientDecision);
            var ownAssets = await scope.OwnAssets(db.EmployeeAssets.AsNoTracking()).Select(x => new { x.Id, x.AssetName, x.Status, x.IssueDate, x.RespondedAtUtc }).ToListAsync(token);
            foreach (var x in ownAssets) Add("OwnAsset", x.Id, "Assets", "Your asset · " + x.AssetName, "Asset allocation · " + x.Status,
                "/Main/EmployeeAssets", x.RespondedAtUtc ?? x.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), x.Status);
            var forms = await db.FeedbackDocuments.AsNoTracking().Where(x => x.PublishedByUserId != userId)
                .Select(x => new { x.Id, x.Title, x.PublishedAtUtc }).ToListAsync(token);
            foreach (var x in forms) Add("PublishedForm", x.Id, "Feedback", "New form · " + x.Title, "Download, complete and upload your response", "/FeedbackDocuments/Details/" + x.Id, x.PublishedAtUtc);
            var slips = await scope.OwnSalarySlips(db.GeneratedSalarySlips.AsNoTracking()).Select(x => new { x.Id, x.Month, x.Year, x.UpdatedAtUtc }).ToListAsync(token);
            foreach (var x in slips) Add("OwnSalary", x.Id, "Salary", "Salary slip available · " + x.Month.ToString("00") + "/" + x.Year, "Your salary slip has been generated or updated", "/Main/SalarySlips", x.UpdatedAtUtc);
            var ownBanks = await scope.OwnBankRequests(db.BankDetailUpdateRequests.AsNoTracking()).Select(x => new { x.Id, x.Status, x.ReviewedAtUtc }).ToListAsync(token);
            foreach (var x in ownBanks) Add("OwnBank", x.Id, "Bank Requests", "Your bank details request updated", x.Status, "/Main/MyBankDetails", x.ReviewedAtUtc!.Value, x.Status);
            var advances = await scope.OwnAdvances(db.ExpenseAdvances.AsNoTracking()).Select(x => new { x.Id, x.Purpose, x.RecordedAtUtc, x.ConfirmationStatus }).ToListAsync(token);
            foreach (var x in advances) Add("OwnAdvance", x.Id, "Expenses", "Expense advance · " + x.Purpose, "Payment recorded · " + x.ConfirmationStatus, "/Expense/Index?view=self", x.RecordedAtUtc, x.ConfirmationStatus);
            var forwarded = await db.ExpenseClaims.AsNoTracking().Where(x => x.ForwardedToEmployeeId == employeeId && x.ForwardedToEmployee != null && x.ForwardedToEmployee.CanReceiveExpenseClaims && (x.WorkflowStage == "Forwarded" || x.WorkflowStage == "Completed"))
                .Select(x => new { x.Id, x.Title, x.WorkflowStage, x.ForwardedAtUtc, x.SubmittedAtUtc }).ToListAsync(token);
            foreach (var x in forwarded) Add("ForwardedExpense", x.Id, "Expenses", "Expense forwarded to you · " + x.Title, "Recipient review · " + x.WorkflowStage,
                "/Expense/Index?view=inbox", x.ForwardedAtUtc ?? x.SubmittedAtUtc, x.WorkflowStage);
        }
        var receipts = (await db.NotificationReadReceipts.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.EventKey).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        return items.Select(x => x with { IsRead = receipts.Contains(x.Key) }).OrderByDescending(x => x.AtUtc).ThenBy(x => x.Key).ToList();
    }

    public async Task MarkReadAsync(int userId, IEnumerable<string> keys, CancellationToken token)
    {
        // Idempotent under multiple tabs. The caller supplies only keys revalidated against its own inbox.
        var eventKeys = keys.Distinct(StringComparer.Ordinal).ToArray();
        if (eventKeys.Length == 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"NotificationReadReceipts\" (\"UserId\", \"EventKey\", \"ReadAtUtc\") SELECT {userId}, unnest({eventKeys}), {DateTime.UtcNow} ON CONFLICT (\"UserId\", \"EventKey\") DO NOTHING", token);
    }
}
