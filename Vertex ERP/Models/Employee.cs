using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class Employee
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(60)]
    public string? LastName { get; set; }

    [Required, MaxLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(20)]
    public string? Gender { get; set; }

    [MaxLength(20)]
    public string? MaritalStatus { get; set; }

    [Phone, MaxLength(20)]
    public string? EmergencyContact { get; set; }

    [MaxLength(120)]
    public string? WorkLocation { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(80)]
    public string? City { get; set; }

    [MaxLength(80)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? PinCode { get; set; }

    [MaxLength(260)]
    public string? PhotoPath { get; set; }

    public DateOnly JoiningDate { get; set; }

    [Required, MaxLength(80)]
    public string Department { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }
    public Department? DepartmentEntity { get; set; }

    [Required, MaxLength(80)]
    public string Designation { get; set; } = string.Empty;

    public int? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }
    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();

    [Required, MaxLength(30)]
    public string EmploymentType { get; set; } = "Full Time";

    [Required, MaxLength(30)]
    public string EmployeeStatus { get; set; } = "Active";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedDate { get; set; }

}
