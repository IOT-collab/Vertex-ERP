using VertexERP.Models;
namespace VertexERP.Services;

public sealed record NotificationInboxScope(string Role, int? EmployeeId)
{
    public bool Management => Role is "HR" or "Admin";
    public bool Manager => Role == "Manager" && EmployeeId.HasValue;
    public bool Personal => EmployeeId.HasValue && Role is "Employee" or "User" or "Manager" or "HR";
    public IQueryable<WorkTask> OwnTasks(IQueryable<WorkTask> query) => query.Where(x => Personal && x.AssigneeId == EmployeeId);
    public IQueryable<LeaveRequest> OwnLeaves(IQueryable<LeaveRequest> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId && (x.SubmittedByHrUserId != null || x.ManagerDecidedAtUtc != null || x.DecidedAtUtc != null || x.Status != "Pending"));
    public IQueryable<QueryTicket> OwnQueries(IQueryable<QueryTicket> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId && (x.UpdatedAtUtc != null || x.Status != "Open"));
    public IQueryable<ExpenseClaim> OwnExpenses(IQueryable<ExpenseClaim> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId && (x.ManagerDecidedAtUtc != null || x.DecidedAtUtc != null || x.ForwardedAtUtc != null || x.RecipientRespondedAtUtc != null));
    public IQueryable<EmployeeAsset> OwnAssets(IQueryable<EmployeeAsset> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId);
    public IQueryable<GeneratedSalarySlip> OwnSalarySlips(IQueryable<GeneratedSalarySlip> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId);
    public IQueryable<BankDetailUpdateRequest> OwnBankRequests(IQueryable<BankDetailUpdateRequest> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId && x.ReviewedAtUtc != null);
    public IQueryable<ExpenseAdvance> OwnAdvances(IQueryable<ExpenseAdvance> query) => query.Where(x => Personal && x.EmployeeId == EmployeeId);
    public bool PrivateSubmissions => Management;
    public bool BankRequests => Role == "HR";
    public IQueryable<LeaveRequest> Leaves(IQueryable<LeaveRequest> query) => query.Where(x => Role == "Admin" || Role == "HR" && x.ApprovalLevel == "HR/Admin" && (x.ManagerDecision == "Approved" || x.ManagerDecision == "Rejected") && x.ManagerDecidedAtUtc != null || Manager && x.AssignedApproverEmployeeId == EmployeeId && x.EmployeeId != EmployeeId);
    public IQueryable<QueryTicket> Queries(IQueryable<QueryTicket> query) => query.Where(x => Management || Manager && x.ReportingManagerId == EmployeeId && x.EmployeeId != EmployeeId);
    public IQueryable<ExpenseClaim> Expenses(IQueryable<ExpenseClaim> query) => query.Where(x => Management ? x.WorkflowStage != "Manager" : Manager && x.ReportingManagerId == EmployeeId && x.EmployeeId != EmployeeId);
    public IQueryable<WorkTask> Reports(IQueryable<WorkTask> query) => query.Where(x => x.UpdatedAtUtc != null && (Management || Manager && x.ManagerId == EmployeeId && x.AssigneeId != EmployeeId));
}
