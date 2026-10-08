using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;

public sealed class AttendanceRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly AttendanceDate { get; set; }
    public TimeOnly CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    [Required, StringLength(300)] public string Reason { get; set; } = "";
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    [Required, StringLength(20)] public string Status { get; set; } = "Pending";
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [StringLength(300)] public string? ReviewReason { get; set; }
}
