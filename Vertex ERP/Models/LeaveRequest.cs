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
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
