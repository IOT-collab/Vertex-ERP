using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace VertexERP.Models;

public class EmployeeDirectoryViewModel
{
    public IReadOnlyList<Employee> Employees { get; init; } = Array.Empty<Employee>();
    public int TotalEmployees { get; init; }
    public int ActiveEmployees { get; init; }
    public int InactiveEmployees { get; init; }
    public int TotalDepartments { get; init; }
    public string? Search { get; init; }
    public string? Department { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();
}

public class EmployeeFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required, StringLength(30), RegularExpression(@"^Vertex-[0-9]{2,}$", ErrorMessage = "Employee Code is invalid.")]
    [Display(Name = "Employee Code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Mobile Number must contain exactly 10 digits.")]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Display(Name = "Date of Birth")]
    public DateOnly? DateOfBirth { get; set; }

    [Display(Name = "Date of Birth")]
    public string? DateOfBirthText { get; set; }

    public string? Gender { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(80)]
    public string? State { get; set; }

    [Required, DataType(DataType.Date)]
    [Display(Name = "Joining Date")]
    public DateOnly JoiningDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required, StringLength(80)]
    public string Department { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Designation { get; set; } = string.Empty;

    [Display(Name = "Reporting Manager")]
    public int? ReportingManagerId { get; set; }

    [Required, Display(Name = "Employment Type")]
    public string EmploymentType { get; set; } = "Full Time";

    [Required, Display(Name = "Employee Status")]
    public string EmployeeStatus { get; set; } = "Active";

    [Display(Name = "Active Employee")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<Employee> Managers { get; set; } = Array.Empty<Employee>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(DateOfBirthText))
        {
            if (!DateOnly.TryParseExact(DateOfBirthText.Trim(), "dd/MM/yyyy", out var parsedDate))
                yield return new ValidationResult("Date of Birth must be a valid date in DD/MM/YYYY format.", new[] { nameof(DateOfBirthText) });
            else
                DateOfBirth = parsedDate;
        }
        else
        {
            DateOfBirth = null;
        }
        if (DateOfBirth.HasValue && DateOfBirth.Value >= today)
            yield return new ValidationResult("Date of Birth must be in the past.", new[] { nameof(DateOfBirthText) });
        if (JoiningDate > today)
            yield return new ValidationResult("Joining date cannot be in the future.", new[] { nameof(JoiningDate) });
        if (DateOfBirth.HasValue && JoiningDate <= DateOfBirth.Value)
            yield return new ValidationResult("Joining date must be after the date of birth.", new[] { nameof(JoiningDate) });
        if (Id > 0 && ReportingManagerId == Id)
            yield return new ValidationResult("An employee cannot report to themselves.", new[] { nameof(ReportingManagerId) });
    }
}

public class HrAddEmployeeViewModel : IValidatableObject
{
    [Required, StringLength(30), RegularExpression(@"^Vertex-[0-9]{2,}$", ErrorMessage = "Employee Code is invalid.")]
    [Display(Name = "Employee ID")]
    public string EmployeeId { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(60)]
    [Display(Name = "Last Name")]
    public string? LastName { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateOnly? DateOfBirth { get; set; }

    [Display(Name = "Date of Birth")]
    public string? DateOfBirthText { get; set; }

    [StringLength(20)] public string? Gender { get; set; }
    [StringLength(20), Display(Name = "Marital Status")] public string? MaritalStatus { get; set; }

    [Required, EmailAddress, StringLength(150)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Mobile Number must contain exactly 10 digits.")]
    [Display(Name = "Phone Number")]
    public string Phone { get; set; } = string.Empty;

    [Phone, StringLength(20), Display(Name = "Emergency Contact")]
    public string? EmergencyContact { get; set; }

    [Required, Display(Name = "Department")] public int? DepartmentId { get; set; }
    [Required, StringLength(80)] public string Designation { get; set; } = string.Empty;

    [Required, DataType(DataType.Date), Display(Name = "Joining Date")]
    public DateOnly JoiningDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(30), Display(Name = "Employment Type")]
    public string? EmploymentType { get; set; }

    [Display(Name = "Reporting Manager")]
    public int? ReportingManagerId { get; set; }

    [StringLength(120), Display(Name = "Work Location")]
    public string? WorkLocation { get; set; }

    [StringLength(300)] public string? Address { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(80)] public string? State { get; set; }

    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "PIN code must contain exactly 6 digits.")]
    [Display(Name = "PIN Code")]
    public string? PinCode { get; set; }

    [Display(Name = "Employee Photo")]
    public IFormFile? EmployeePhoto { get; set; }

    public IReadOnlyList<Employee> Managers { get; set; } = Array.Empty<Employee>();
    public IReadOnlyList<Department> Departments { get; set; } = Array.Empty<Department>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(DateOfBirthText))
        {
            if (!DateOnly.TryParseExact(DateOfBirthText.Trim(), "dd/MM/yyyy", out var parsedDate))
                yield return new ValidationResult("Date of Birth must be a valid date in DD/MM/YYYY format.", new[] { nameof(DateOfBirthText) });
            else
                DateOfBirth = parsedDate;
        }
        else
        {
            DateOfBirth = null;
        }
        if (DateOfBirth.HasValue && DateOfBirth.Value >= today)
            yield return new ValidationResult("Date of Birth must be in the past.", new[] { nameof(DateOfBirthText) });
        if (JoiningDate > today)
            yield return new ValidationResult("Joining date cannot be in the future.", new[] { nameof(JoiningDate) });
        if (DateOfBirth.HasValue && JoiningDate <= DateOfBirth.Value)
            yield return new ValidationResult("Joining date must be after the date of birth.", new[] { nameof(JoiningDate) });
    }
}
