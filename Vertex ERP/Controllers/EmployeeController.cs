using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public class EmployeeController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly BankAccountProtectionService _bankProtection;

    public EmployeeController(ApplicationDbContext dbContext, IWebHostEnvironment environment, BankAccountProtectionService bankProtection)
    {
        _dbContext = dbContext;
        _environment = environment;
        _bankProtection = bankProtection;
    }

    [HttpGet]
    public IActionResult Index(string? search, string? department, string? status)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.ReportingManager)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(employee =>
                employee.EmployeeCode.ToLower().Contains(term) ||
                employee.FirstName.ToLower().Contains(term) ||
                (employee.LastName != null && employee.LastName.ToLower().Contains(term)) ||
                employee.Email.ToLower().Contains(term) ||
                employee.Department.ToLower().Contains(term) ||
                employee.Designation.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(department))
            query = query.Where(employee => employee.Department == department);

        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(employee => employee.IsActive);
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            query = query.Where(employee => !employee.IsActive);

        var model = new EmployeeDirectoryViewModel
        {
            Employees = query.OrderBy(employee => employee.FirstName).ThenBy(employee => employee.LastName).ToList(),
            TotalEmployees = _dbContext.Employees.Count(),
            ActiveEmployees = _dbContext.Employees.Count(employee => employee.IsActive),
            InactiveEmployees = _dbContext.Employees.Count(employee => !employee.IsActive),
            TotalDepartments = _dbContext.Employees.Select(employee => employee.Department).Distinct().Count(),
            Departments = _dbContext.Employees.Select(employee => employee.Department).Distinct().OrderBy(name => name).ToList(),
            Search = search,
            Department = department,
            Status = status
        };

        return View("~/Views/Hr/EmployeeDashboard.cshtml", model);
    }

    [HttpGet]
    public IActionResult Details(int id)
    {
        var employee = _dbContext.Employees
            .AsNoTracking()
            .Include(item => item.ReportingManager)
            .FirstOrDefault(item => item.Id == id);
        return employee == null ? NotFound() : View(employee);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return RedirectToAction("HrAddEmp", "Hr");
    }

    [HttpGet]
    public async Task<IActionResult> LoginAccess(int id)
    {
        var employee = await _dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (employee == null) return NotFound();
        var account = await _dbContext.AppUsers.AsNoTracking().FirstOrDefaultAsync(user => user.EmployeeId == id);
        return View(new EmployeeLoginAccessViewModel
        {
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            EmployeeName = employee.FullName,
            Username = account?.Username ?? employee.EmployeeCode.ToLowerInvariant(),
            MustChangePassword = account?.MustChangePassword ?? true,
            HasExistingAccount = account != null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginAccess(EmployeeLoginAccessViewModel model)
    {
        var employee = await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == model.EmployeeId);
        if (employee == null) return NotFound();
        var normalizedUsername = DatabaseInitializer.NormalizeUsername(model.Username);
        if (await _dbContext.AppUsers.AnyAsync(user => user.EmployeeId != model.EmployeeId && user.NormalizedUsername == normalizedUsername))
            ModelState.AddModelError(nameof(model.Username), "Login username already exists.");

        if (!ModelState.IsValid)
        {
            model.EmployeeCode = employee.EmployeeCode;
            model.EmployeeName = employee.FullName;
            model.HasExistingAccount = await _dbContext.AppUsers.AnyAsync(user => user.EmployeeId == model.EmployeeId);
            return View(model);
        }

        var account = await _dbContext.AppUsers.FirstOrDefaultAsync(user => user.EmployeeId == model.EmployeeId);
        if (account == null)
        {
            account = new AppUser { EmployeeId = employee.Id, CreatedAt = DateTime.UtcNow };
            _dbContext.AppUsers.Add(account);
        }
        account.Username = model.Username.Trim();
        account.NormalizedUsername = normalizedUsername;
        var password = model.TemporaryPassword.Trim();
        var passwordHash = PasswordHashService.HashPassword(password);
        if (!PasswordHashService.VerifyPassword(password, passwordHash))
        {
            ModelState.AddModelError(nameof(model.TemporaryPassword), "Unable to create a valid login password. Please try again.");
            model.EmployeeCode = employee.EmployeeCode;
            model.EmployeeName = employee.FullName;
            model.HasExistingAccount = account.Id > 0;
            return View(model);
        }
        account.PasswordHash = passwordHash;
        account.Role = AccountRoleService.Normalize(account.Role) == AccountRoleService.Manager
            ? AccountRoleService.Manager
            : AccountRoleService.Employee;
        account.FullName = employee.FullName;
        account.IsActive = true;
        account.MustChangePassword = model.MustChangePassword;
        await _dbContext.SaveChangesAsync();

        TempData["EmployeeMessage"] = $"Login credentials for {employee.EmployeeCode} saved successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(EmployeeFormViewModel model)
    {
        ValidateUniqueFields(model);
        if (!ModelState.IsValid)
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));

        var employee = new Employee();
        ApplyForm(employee, model);
        _dbContext.Employees.Add(employee);

        if (!TrySave("The employee could not be created because the data conflicts with an existing record."))
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));

        TempData["EmployeeMessage"] = "Employee created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        var employee = _dbContext.Employees.Find(id);
        if (employee == null) return NotFound();
        var model = ToForm(employee);
        var bank = _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefault(item => item.EmployeeId == id);
        if (bank != null)
        {
            model.BankAccountHolderName = bank.AccountHolderName; model.BankName = bank.BankName; model.BankIfscCode = bank.IfscCode;
            model.BankBranchName = bank.BranchName; model.BankAccountType = bank.AccountType; model.PanNumber = bank.PanNumber;
            model.UanNumber = bank.UanNumber; model.EsicNumber = bank.EsicNumber; model.UpiId = bank.UpiId;
            model.MaskedBankAccountNumber = $"XXXX XXXX {bank.AccountLastFour}";
        }
        var salary = _dbContext.EmployeeSalaryDetails.AsNoTracking().FirstOrDefault(item => item.EmployeeId == id);
        if (salary != null) { model.BasicSalary=salary.BasicSalary; model.HouseRentAllowance=salary.HouseRentAllowance; model.ConveyanceAllowance=salary.ConveyanceAllowance; model.SpecialAllowance=salary.SpecialAllowance; model.ProvidentFund=salary.ProvidentFund; model.ProfessionalTax=salary.ProfessionalTax; model.Tds=salary.Tds; model.OtherDeductions=salary.OtherDeductions; model.PfNumber=salary.PfNumber; model.PfUan=salary.PfUan; model.SalaryEffectiveFrom=salary.EffectiveFrom; model.HasSalaryDetails=true; }
        return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EmployeeFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var employee = _dbContext.Employees.Find(id);
        if (employee == null) return NotFound();

        ValidateUniqueFields(model);
        var photoExtension = await ValidatePhotoAsync(model.EmployeePhoto);
        if (!ModelState.IsValid)
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));

        var previousPhotoPath = employee.PhotoPath;
        string? newPhotoPath = null;
        if (model.EmployeePhoto != null && photoExtension != null)
        {
            newPhotoPath = await SavePhotoAsync(model.EmployeePhoto, photoExtension);
            employee.PhotoPath = newPhotoPath;
        }
        ApplyForm(employee, model, preserveEmployeeCode: false);
        employee.UpdatedDate = DateTime.UtcNow;

        if (!TrySave("The employee could not be updated because the data conflicts with an existing record."))
        {
            DeletePhotoIfPresent(newPhotoPath);
            employee.PhotoPath = previousPhotoPath;
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));
        }

        var existingBank = await _dbContext.EmployeeBankDetails.FirstOrDefaultAsync(item => item.EmployeeId == id);
        if (!string.IsNullOrWhiteSpace(model.BankAccountNumber) || existingBank != null)
        {
            var bank = existingBank;
            if (bank == null) { bank = new EmployeeBankDetail { EmployeeId = id }; _dbContext.EmployeeBankDetails.Add(bank); }
            bank.AccountHolderName = model.BankAccountHolderName!.Trim(); bank.BankName = model.BankName!.Trim();
            if (!string.IsNullOrWhiteSpace(model.BankAccountNumber))
            {
                var account = model.BankAccountNumber.Trim();
                bank.ProtectedAccountNumber = _bankProtection.Protect(account); bank.AccountLastFour = account[^4..];
            }
            bank.IfscCode = model.BankIfscCode!.Trim().ToUpperInvariant(); bank.BranchName = Clean(model.BankBranchName);
            bank.AccountType = Clean(model.BankAccountType) ?? "Savings"; bank.PanNumber = Clean(model.PanNumber)?.ToUpperInvariant();
            bank.UanNumber = Clean(model.UanNumber); bank.EsicNumber = Clean(model.EsicNumber); bank.UpiId = Clean(model.UpiId);
            bank.IsVerified = true; bank.VerifiedAtUtc = DateTime.UtcNow; bank.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
        if (model.BasicSalary > 0 || model.HouseRentAllowance > 0 || model.ConveyanceAllowance > 0 || model.SpecialAllowance > 0 || model.HasSalaryDetails)
        {
            var salary = await _dbContext.EmployeeSalaryDetails.FirstOrDefaultAsync(item => item.EmployeeId == id);
            if (salary == null) { salary = new EmployeeSalaryDetail { EmployeeId = id }; _dbContext.EmployeeSalaryDetails.Add(salary); }
            salary.BasicSalary=model.BasicSalary; salary.HouseRentAllowance=model.HouseRentAllowance; salary.ConveyanceAllowance=model.ConveyanceAllowance; salary.SpecialAllowance=model.SpecialAllowance; salary.ProvidentFund=model.ProvidentFund; salary.ProfessionalTax=model.ProfessionalTax; salary.Tds=model.Tds; salary.OtherDeductions=model.OtherDeductions; salary.PfNumber=Clean(model.PfNumber); salary.PfUan=Clean(model.PfUan); salary.EffectiveFrom=model.SalaryEffectiveFrom; salary.IsActive=true; salary.UpdatedAtUtc=DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        if (newPhotoPath != null) DeletePhotoIfPresent(previousPhotoPath);

        TempData["EmployeeMessage"] = "Employee updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Delete(int id)
    {
        var employee = _dbContext.Employees.AsNoTracking().FirstOrDefault(item => item.Id == id);
        return employee == null ? NotFound() : View(employee);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        string? photoPath = null;
        var employeeFound = false;
        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var employee = await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == id);
            if (employee == null) return;

            employeeFound = true;
            photoPath = employee.PhotoPath;
            var directReports = await _dbContext.Employees
                .Where(item => item.ReportingManagerId == id)
                .ToListAsync();
            foreach (var directReport in directReports)
            {
                directReport.ReportingManagerId = null;
                directReport.UpdatedDate = DateTime.UtcNow;
            }

            var managedDepartments = await _dbContext.Departments
                .Where(department => department.ManagerId == id)
                .ToListAsync();
            foreach (var department in managedDepartments)
            {
                department.ManagerId = null;
                department.UpdatedDate = DateTime.UtcNow;
            }

            // Remove every employee-owned record before deleting the employee.
            // Several of these relationships intentionally use RESTRICT in the database.
            var loginAccounts = await _dbContext.AppUsers
                .Where(user => user.EmployeeId == id)
                .ToListAsync();
            var deviceMappings = await _dbContext.EmployeeDeviceMappings
                .Where(mapping => mapping.EmployeeId == id)
                .ToListAsync();
            var attendanceLogs = await _dbContext.AttendanceLogs
                .Where(log => log.EmployeeId == id)
                .ToListAsync();
            var employeeTasks = await _dbContext.WorkTasks
                .Where(task => task.ManagerId == id || task.AssigneeId == id)
                .ToListAsync();

            _dbContext.AppUsers.RemoveRange(loginAccounts);
            _dbContext.EmployeeDeviceMappings.RemoveRange(deviceMappings);
            _dbContext.AttendanceLogs.RemoveRange(attendanceLogs);
            _dbContext.WorkTasks.RemoveRange(employeeTasks);

            await _dbContext.SaveChangesAsync();
            _dbContext.Employees.Remove(employee);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        if (!employeeFound) return NotFound();
        DeletePhotoIfPresent(photoPath);
        TempData["EmployeeMessage"] = "Employee deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateUniqueFields(EmployeeFormViewModel model)
    {
        var code = model.EmployeeCode.Trim();
        var email = model.Email.Trim().ToLowerInvariant();
        var phoneNumber = model.PhoneNumber.Trim();
        if (_dbContext.Employees.Any(employee => employee.Id != model.Id && employee.EmployeeCode.ToLower() == code.ToLower()))
            ModelState.AddModelError(nameof(model.EmployeeCode), "Employee ID already exists.");
        if (_dbContext.Employees.Any(employee => employee.Id != model.Id && employee.Email == email))
            ModelState.AddModelError(nameof(model.Email), "Email address already exists.");
        if (_dbContext.Employees.Any(employee => employee.Id != model.Id && employee.PhoneNumber == phoneNumber))
            ModelState.AddModelError(nameof(model.PhoneNumber), "Mobile Number already exists.");
        if (model.Id > 0 && CreatesReportingCycle(model.Id, model.ReportingManagerId))
            ModelState.AddModelError(nameof(model.ReportingManagerId), "This reporting manager assignment would create a circular reporting chain.");
    }

    private bool CreatesReportingCycle(int employeeId, int? managerId)
    {
        var visited = new HashSet<int>();
        while (managerId.HasValue)
        {
            if (managerId.Value == employeeId || !visited.Add(managerId.Value)) return true;
            managerId = _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.Id == managerId.Value)
                .Select(employee => employee.ReportingManagerId)
                .FirstOrDefault();
        }
        return false;
    }

    private EmployeeFormViewModel PopulateManagers(EmployeeFormViewModel model)
    {
        model.Managers = _dbContext.Employees.AsNoTracking()
            .Where(employee => employee.IsActive && employee.Id != model.Id)
            .OrderBy(employee => employee.FirstName)
            .ThenBy(employee => employee.LastName)
            .ToList();
        return model;
    }

    private bool TrySave(string errorMessage)
    {
        try
        {
            _dbContext.SaveChanges();
            return true;
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, errorMessage);
            return false;
        }
    }

    private static void ApplyForm(Employee employee, EmployeeFormViewModel model, bool preserveEmployeeCode = false)
    {
        if (!preserveEmployeeCode)
            employee.EmployeeCode = model.EmployeeCode.Trim();
        employee.FirstName = model.FirstName.Trim();
        employee.LastName = Clean(model.LastName);
        employee.FullName = $"{employee.FirstName} {employee.LastName}".Trim();
        employee.Email = model.Email.Trim().ToLowerInvariant();
        employee.PhoneNumber = model.PhoneNumber.Trim();
        employee.DateOfBirth = model.DateOfBirth;
        employee.Gender = Clean(model.Gender);
        employee.MaritalStatus = Clean(model.MaritalStatus);
        employee.EmergencyContact = Clean(model.EmergencyContact);
        employee.Address = Clean(model.Address);
        employee.City = Clean(model.City);
        employee.State = Clean(model.State);
        employee.PinCode = Clean(model.PinCode);
        employee.WorkLocation = Clean(model.WorkLocation);
        employee.JoiningDate = model.JoiningDate;
        employee.Department = model.Department.Trim();
        employee.Designation = model.Designation.Trim();
        employee.ReportingManagerId = model.ReportingManagerId;
        employee.EmploymentType = model.EmploymentType;
        employee.IsActive = model.IsActive && model.EmployeeStatus != "Inactive";
        employee.EmployeeStatus = employee.IsActive ? model.EmployeeStatus : "Inactive";
    }

    private static EmployeeFormViewModel ToForm(Employee employee) => new()
    {
        Id = employee.Id,
        EmployeeCode = employee.EmployeeCode,
        FirstName = employee.FirstName,
        LastName = employee.LastName ?? string.Empty,
        Email = employee.Email,
        PhoneNumber = employee.PhoneNumber,
        DateOfBirth = employee.DateOfBirth,
        DateOfBirthText = employee.DateOfBirth?.ToString("dd/MM/yyyy"),
        Gender = employee.Gender,
        MaritalStatus = employee.MaritalStatus,
        EmergencyContact = employee.EmergencyContact,
        Address = employee.Address,
        City = employee.City,
        State = employee.State,
        PinCode = employee.PinCode,
        WorkLocation = employee.WorkLocation,
        PhotoPath = employee.PhotoPath,
        JoiningDate = employee.JoiningDate,
        Department = employee.Department,
        Designation = employee.Designation,
        ReportingManagerId = employee.ReportingManagerId,
        EmploymentType = employee.EmploymentType,
        EmployeeStatus = employee.EmployeeStatus,
        IsActive = employee.IsActive
    };

    private string GenerateEmployeeCode()
    {
        var number = _dbContext.Database
            .SqlQueryRaw<long>("SELECT nextval('\"EmployeeCodeSequence\"') AS \"Value\"")
            .Single();
        return $"Vertex-{number:D2}";
    }

    private void DeletePhotoIfPresent(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var physicalPath = Path.Combine(_environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
    }

    private async Task<string?> ValidatePhotoAsync(IFormFile? photo)
    {
        if (photo == null) return null;
        if (photo.Length == 0 || photo.Length > 5 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(EmployeeFormViewModel.EmployeePhoto), "Photo must be smaller than 5 MB.");
            return null;
        }
        var header = new byte[8];
        await using var stream = photo.OpenReadStream();
        var count = await stream.ReadAsync(header.AsMemory(0, header.Length));
        var jpeg = count >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var png = count >= 8 && header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        if (jpeg) return ".jpg";
        if (png) return ".png";
        ModelState.AddModelError(nameof(EmployeeFormViewModel.EmployeePhoto), "Please select a valid JPG or PNG image.");
        return null;
    }

    private async Task<string> SavePhotoAsync(IFormFile photo, string extension)
    {
        var directory = Path.Combine(_environment.WebRootPath, "uploads", "employees");
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var target = new FileStream(Path.Combine(directory, fileName), FileMode.CreateNew);
        await photo.CopyToAsync(target);
        return $"/uploads/employees/{fileName}";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
