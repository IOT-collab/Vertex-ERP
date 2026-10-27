using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class LeaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, MaxLength(30)] public string LeaveType { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public bool IsHalfDay { get; set; }
    public bool IsExtraFacility { get; set; }
    public TimeOnly? ShortLeaveFrom { get; set; }
    public TimeOnly? ShortLeaveTo { get; set; }
    [MaxLength(10)] public string? HalfDaySession { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Status { get; set; } = "Pending";
    [Required, MaxLength(30)] public string ApprovalLevel { get; set; } = "Manager";
    public int? AssignedApproverEmployeeId { get; set; }
    public Employee? AssignedApproverEmployee { get; set; }
    public int? DecidedByUserId { get; set; }
    public AppUser? DecidedByUser { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    [MaxLength(500)] public string? DecisionNote { get; set; }
    public int? ManagerDecidedByUserId { get; set; }
    [MaxLength(200)] public string? ManagerDecidedByName { get; set; }
    [MaxLength(20)] public string? ManagerDecision { get; set; }
    public DateTime? ManagerDecidedAtUtc { get; set; }
    [MaxLength(500)] public string? ManagerDecisionNote { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string DisplayStatus => Status == "Pending" ? $"Pending {((IsHalfDay || IsExtraFacility) && ApprovalLevel == "HR/Admin" ? "HR" : ApprovalLevel)}" : Status;
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
