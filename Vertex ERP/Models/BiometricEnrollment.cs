using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

// WDMS emp_code is an enrollment identifier, not the ERP EmployeeCode or WDMS id.
public sealed class BiometricEnrollment
{
    [Key, MaxLength(50)] public string EnrollCode { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string FullName { get; set; } = string.Empty;
    [MaxLength(120)] public string? WdmsName { get; set; }
    public bool IsSheetReference { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Pending";
    [MaxLength(300)] public string? ReviewReason { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}
