using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class AddDepartmentViewModel
{
    public int Id { get; set; }

    [Required, StringLength(80), Display(Name = "Department Name")]
    public string DepartmentName { get; set; } = string.Empty;

    [Required, StringLength(100), Display(Name = "Department Code")]
    public string DepartmentCode { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "Status")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Manager / Department Manager")]
    public int? ManagerId { get; set; }

    public IReadOnlyList<Employee> Managers { get; set; } = Array.Empty<Employee>();
}

public class DepartmentOverviewViewModel
{
    public IReadOnlyList<DepartmentOverviewItem> Departments { get; init; } = Array.Empty<DepartmentOverviewItem>();
    public int TotalDepartments { get; init; }
    public int TotalEmployees { get; init; }
    public int ActiveDepartments { get; init; }
}

public class DepartmentOverviewItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int EmployeeCount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ManagerName { get; init; } = "Not Assigned";
}
