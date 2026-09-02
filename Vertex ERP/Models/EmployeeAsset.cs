using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class EmployeeAsset
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [Required, StringLength(100)] public string AssetTag { get; set; } = string.Empty;
    [Required, StringLength(150)] public string AssetName { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Category { get; set; } = string.Empty;
    [StringLength(100)] public string? SerialNumber { get; set; }
    public DateOnly IssueDate { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}
public sealed class IssueAssetViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select an employee.")] public int EmployeeId { get; set; }
    [Required, StringLength(100)] public string AssetTag { get; set; } = string.Empty;
    [Required, StringLength(150)] public string AssetName { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Category { get; set; } = string.Empty;
    [StringLength(100)] public string? SerialNumber { get; set; }
    [Required] public DateOnly? IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [StringLength(500)] public string? Notes { get; set; }
    public IReadOnlyList<Employee> Employees { get; set; } = Array.Empty<Employee>();
}
public sealed class EmployeeAssetsViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<EmployeeAsset> Assets { get; init; } = Array.Empty<EmployeeAsset>();
}
