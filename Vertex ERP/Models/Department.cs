using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class Department
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string DepartmentName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DepartmentCode { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedDate { get; set; }

    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
