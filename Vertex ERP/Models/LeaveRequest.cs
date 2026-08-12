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
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Status { get; set; } = "Pending";
    [Required, MaxLength(30)] public string ApprovalLevel { get; set; } = "Manager";
    public int? AssignedApproverEmployeeId { get; set; }
    public Employee? AssignedApproverEmployee { get; set; }
    public int? DecidedByUserId { get; set; }
    public AppUser? DecidedByUser { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    [MaxLength(500)] public string? DecisionNote { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
