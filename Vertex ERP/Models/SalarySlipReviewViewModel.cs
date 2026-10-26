using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class SalarySlipReviewViewModel
{
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string EmployeeName { get; set; } = "";
    [StringLength(300)] public string? DeductionNote { get; set; }
    public Dictionary<string, decimal> Values { get; set; } = new();
}
