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

public class EmployeeSelfServiceViewModel
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    public string ShiftTime { get; init; } = string.Empty;
    public string LeaveBalanceDays { get; init; } = string.Empty;
    public string LoggedHoursThisMonth { get; init; } = string.Empty;
    public string EmploymentType { get; init; } = string.Empty;
    public string EmployeeId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string JoiningDate { get; init; } = string.Empty;
    public string ManagerName { get; init; } = string.Empty;
    public string CasualLeaveCount { get; init; } = string.Empty;
    public string SickLeaveCount { get; init; } = string.Empty;
    public string EarnedLeaveCount { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string RoutingCode { get; init; } = string.Empty;
    public string PhotoPath { get; init; } = string.Empty;

    public static EmployeeSelfServiceViewModel FromEmployee(Employee employee)
    {
        var firstInitial = employee.FirstName.FirstOrDefault();
        var lastInitial = employee.LastName?.FirstOrDefault() ?? '\0';
        return new EmployeeSelfServiceViewModel
        {
            Id = employee.Id,
            FullName = employee.FullName,
            Designation = employee.Designation,
            Department = employee.Department,
            Initials = lastInitial == '\0' ? firstInitial.ToString().ToUpperInvariant() : $"{firstInitial}{lastInitial}".ToUpperInvariant(),
            Status = employee.EmployeeStatus,
            EmploymentType = employee.EmploymentType,
            EmployeeId = employee.EmployeeCode,
            Email = employee.Email,
            Phone = employee.PhoneNumber,
            JoiningDate = employee.JoiningDate.ToString("dd MMM yyyy"),
            ManagerName = employee.ReportingManager?.FullName ?? "Not assigned",
            PhotoPath = employee.PhotoPath ?? string.Empty
        };
    }
}

public class EmployeeLoginAccessViewModel
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Username can contain letters, numbers, dot, underscore and hyphen only.")]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [Display(Name = "Temporary Password")]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(TemporaryPassword), ErrorMessage = "Password and confirmation do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; } = true;

    public bool HasExistingAccount { get; set; }
}

public class EmployeeFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Employee ID is required."), StringLength(30, ErrorMessage = "Employee ID cannot exceed 30 characters.")]
    [Display(Name = "Employee ID")]
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

    [StringLength(20), Display(Name = "Marital Status")]
    public string? MaritalStatus { get; set; }

    [Phone, StringLength(20), Display(Name = "Emergency Contact")]
    public string? EmergencyContact { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(80)]
    public string? State { get; set; }

    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "PIN must contain exactly 6 digits.")]
    [Display(Name = "PIN")]
    public string? PinCode { get; set; }

    [StringLength(120), Display(Name = "Work Location")]
    public string? WorkLocation { get; set; }

    public string? PhotoPath { get; set; }

    [Display(Name = "Employee Photo")]
    public IFormFile? EmployeePhoto { get; set; }

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

    [StringLength(120), Display(Name = "Account Holder Name")]
    public string? BankAccountHolderName { get; set; }
    [StringLength(120), Display(Name = "Bank Name")]
    public string? BankName { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits."), Display(Name = "New Account Number")]
    public string? BankAccountNumber { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits."), Compare(nameof(BankAccountNumber), ErrorMessage = "Account numbers do not match."), Display(Name = "Confirm Account Number")]
    public string? ConfirmBankAccountNumber { get; set; }
    [Display(Name = "IFSC Code")]
    public string? BankIfscCode { get; set; }
    [StringLength(120), Display(Name = "Branch Name")] public string? BankBranchName { get; set; }
    [StringLength(30), Display(Name = "Account Type")] public string? BankAccountType { get; set; } = "Savings";
    [StringLength(20), Display(Name = "PAN Number")] public string? PanNumber { get; set; }
    [StringLength(30), Display(Name = "UAN / PF Number")] public string? UanNumber { get; set; }
    [StringLength(30), Display(Name = "ESIC Number")] public string? EsicNumber { get; set; }
    [StringLength(100), Display(Name = "UPI ID")] public string? UpiId { get; set; }
    public string? MaskedBankAccountNumber { get; set; }
    [Range(0, 100000000), Display(Name="Basic Salary")] public decimal BasicSalary { get; set; }
    [Range(0, 100000000), Display(Name="House Rent Allowance")] public decimal HouseRentAllowance { get; set; }
    [Range(0, 100000000), Display(Name="Conveyance Allowance")] public decimal ConveyanceAllowance { get; set; }
    [Range(0, 100000000), Display(Name="Special Allowance")] public decimal SpecialAllowance { get; set; }
    [Range(0, 100000000), Display(Name="Provident Fund Deduction")] public decimal ProvidentFund { get; set; }
    [Range(0, 100000000), Display(Name="Professional Tax")] public decimal ProfessionalTax { get; set; }
    [Range(0, 100000000), Display(Name="TDS")] public decimal Tds { get; set; }
    [Range(0, 100000000), Display(Name="Other Deductions")] public decimal OtherDeductions { get; set; }
    [StringLength(50), Display(Name="PF Number")] public string? PfNumber { get; set; }
    [StringLength(30), Display(Name="PF UAN")] public string? PfUan { get; set; }
    [Display(Name="Salary Effective From")] public DateOnly SalaryEffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool HasSalaryDetails { get; set; }

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
            yield return new ValidationResult("Date of Birth must be in the past.", new[] { nameof(DateOfBirth) });
        if (JoiningDate > today)
            yield return new ValidationResult("Joining date cannot be in the future.", new[] { nameof(JoiningDate) });
        if (DateOfBirth.HasValue && JoiningDate <= DateOfBirth.Value)
            yield return new ValidationResult("Joining date must be after the date of birth.", new[] { nameof(JoiningDate) });
        if (Id > 0 && ReportingManagerId == Id)
            yield return new ValidationResult("An employee cannot report to themselves.", new[] { nameof(ReportingManagerId) });
        if (!string.IsNullOrWhiteSpace(BankAccountNumber) && string.IsNullOrWhiteSpace(BankAccountHolderName))
            yield return new ValidationResult("Account holder name is required when updating bank details.", new[] { nameof(BankAccountHolderName) });
        if (!string.IsNullOrWhiteSpace(BankAccountNumber) && string.IsNullOrWhiteSpace(BankName))
            yield return new ValidationResult("Bank name is required when updating bank details.", new[] { nameof(BankName) });
        if (!string.IsNullOrWhiteSpace(BankAccountNumber) && string.IsNullOrWhiteSpace(BankIfscCode))
            yield return new ValidationResult("IFSC code is required when updating bank details.", new[] { nameof(BankIfscCode) });
        if (!string.IsNullOrWhiteSpace(BankIfscCode) && !System.Text.RegularExpressions.Regex.IsMatch(BankIfscCode.Trim(), @"^[A-Za-z]{4}0[A-Za-z0-9]{6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            yield return new ValidationResult("Enter a valid 11-character IFSC code (example: BARB0MURADN).", new[] { nameof(BankIfscCode) });
    }
}

public class EmployeeBankDraft
{
    [StringLength(120)] public string? BankAccountHolderName { get; set; }
    [StringLength(120)] public string? BankName { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits.")] public string? BankAccountNumber { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits."), Compare(nameof(BankAccountNumber), ErrorMessage = "Bank account numbers do not match.")] public string? ConfirmBankAccountNumber { get; set; }
    [RegularExpression(@"^$|^[A-Za-z]{4}0[A-Za-z0-9]{6}$", ErrorMessage = "Enter a valid IFSC code.")] public string? BankIfscCode { get; set; }
    [StringLength(120)] public string? BankBranchName { get; set; }
    [StringLength(30)] public string? BankAccountType { get; set; }
    [StringLength(20)] public string? PanNumber { get; set; }
    [StringLength(30)] public string? UanNumber { get; set; }
    [StringLength(30)] public string? EsicNumber { get; set; }
    [StringLength(100)] public string? UpiId { get; set; }
}

public class EmployeeSalaryDraft
{
    [Range(0, 100000000)] public decimal BasicSalary { get; set; }
    [Range(0, 100000000)] public decimal HouseRentAllowance { get; set; }
    [Range(0, 100000000)] public decimal ConveyanceAllowance { get; set; }
    [Range(0, 100000000)] public decimal SpecialAllowance { get; set; }
    [Range(0, 100000000)] public decimal ProvidentFund { get; set; }
    [Range(0, 100000000)] public decimal ProfessionalTax { get; set; }
    [Range(0, 100000000)] public decimal Tds { get; set; }
    [Range(0, 100000000)] public decimal OtherDeductions { get; set; }
    [StringLength(50)] public string? PfNumber { get; set; }
    [StringLength(30)] public string? PfUan { get; set; }
    public DateOnly SalaryEffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public class HrAddEmployeeViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Employee ID is required."), StringLength(30, ErrorMessage = "Employee ID cannot exceed 30 characters.")]
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

    [Required(ErrorMessage = "Emergency Contact is required.")]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Emergency Contact must contain exactly 10 digits.")]
    [Display(Name = "Emergency Contact")]
    public string EmergencyContact { get; set; } = string.Empty;

    [Required, Display(Name = "Department")] public int? DepartmentId { get; set; }
    [Required, StringLength(80)] public string Designation { get; set; } = string.Empty;

    [Required, RegularExpression("^(Employee|Manager)$", ErrorMessage = "Please select Employee or Manager.")]
    [Display(Name = "Position")]
    public string Position { get; set; } = "Employee";

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

    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Username can contain letters, numbers, dot, underscore and hyphen only.")]
    [Display(Name = "Login Username")]
    public string LoginUsername { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [Display(Name = "Temporary Password")]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(TemporaryPassword), ErrorMessage = "Password and confirmation do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Employee must change password on first login")]
    public bool MustChangePassword { get; set; } = true;

    [StringLength(120)] public string? BankAccountHolderName { get; set; }
    [StringLength(120)] public string? BankName { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits.")] public string? BankAccountNumber { get; set; }
    [RegularExpression(@"^$|^[0-9]{8,20}$", ErrorMessage = "Account number must contain 8 to 20 digits."), Compare(nameof(BankAccountNumber), ErrorMessage = "Bank account numbers do not match.")] public string? ConfirmBankAccountNumber { get; set; }
    [RegularExpression(@"^$|^[A-Za-z]{4}0[A-Za-z0-9]{6}$", ErrorMessage = "Enter a valid IFSC code.")] public string? BankIfscCode { get; set; }
    [StringLength(120)] public string? BankBranchName { get; set; }
    [StringLength(30)] public string? BankAccountType { get; set; }
    [StringLength(20)] public string? PanNumber { get; set; }
    [StringLength(30)] public string? UanNumber { get; set; }
    [StringLength(30)] public string? EsicNumber { get; set; }
    [StringLength(100)] public string? UpiId { get; set; }
    [Range(0, 100000000)] public decimal BasicSalary { get; set; }
    [Range(0, 100000000)] public decimal HouseRentAllowance { get; set; }
    [Range(0, 100000000)] public decimal ConveyanceAllowance { get; set; }
    [Range(0, 100000000)] public decimal SpecialAllowance { get; set; }
    [Range(0, 100000000)] public decimal ProvidentFund { get; set; }
    [Range(0, 100000000)] public decimal ProfessionalTax { get; set; }
    [Range(0, 100000000)] public decimal Tds { get; set; }
    [Range(0, 100000000)] public decimal OtherDeductions { get; set; }
    [StringLength(50)] public string? PfNumber { get; set; }
    [StringLength(30)] public string? PfUan { get; set; }
    public DateOnly SalaryEffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public IReadOnlyList<Employee> Managers { get; set; } = Array.Empty<Employee>();
    public IReadOnlyList<Department> Departments { get; set; } = Array.Empty<Department>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (DateOfBirth.HasValue && DateOfBirth.Value >= today)
            yield return new ValidationResult("Date of Birth must be in the past.", new[] { nameof(DateOfBirth) });
        if (JoiningDate > today)
            yield return new ValidationResult("Joining date cannot be in the future.", new[] { nameof(JoiningDate) });
        if (DateOfBirth.HasValue && JoiningDate <= DateOfBirth.Value)
            yield return new ValidationResult("Joining date must be after the date of birth.", new[] { nameof(JoiningDate) });
    }
}
