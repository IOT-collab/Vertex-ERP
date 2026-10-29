using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;

public sealed class HrLeaveApplication
{
    [Range(1,int.MaxValue)] public int EmployeeId { get; set; }
    [Required,MaxLength(30)] public string LeaveType { get; set; } = "EL";
    [Required] public DateOnly? FromDate { get; set; } = DateOnly.FromDateTime(VertexERP.Services.FieldAttendanceClock.Now);
    [Required] public DateOnly? ToDate { get; set; } = DateOnly.FromDateTime(VertexERP.Services.FieldAttendanceClock.Now);
    [Required,StringLength(500)] public string Reason { get; set; } = "";
    public string? HalfDaySession { get; set; }
    public TimeOnly? ShortLeaveFrom { get; set; }
    public TimeOnly? ShortLeaveTo { get; set; }
}
public sealed record HrLeaveEmployeeOption(int Id,string Name,string Code,string Department,string? ManagerName);
public sealed class HrLeavePage
{
    public bool CanSubmit { get; init; }
    public HrLeaveApplication Input { get; init; } = new();
    public IReadOnlyList<HrLeaveEmployeeOption> Employees { get; init; } = [];
    public IReadOnlyList<LeaveRequest> Requests { get; init; } = [];
}
