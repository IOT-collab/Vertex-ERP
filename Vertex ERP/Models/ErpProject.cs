using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ErpProject
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string ProjectCode { get; set; } = "";
    [Required, MaxLength(160)] public string ProjectName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    [Required, RegularExpression("^(Planning|Active|On Hold|Completed|Cancelled)$"), MaxLength(20)]
    public string Status { get; set; } = "Planning";
    [MaxLength(2000)] public string? Description { get; set; }
}
