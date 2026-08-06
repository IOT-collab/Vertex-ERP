using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public class EmployeeController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public EmployeeController(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
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
        return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(new EmployeeFormViewModel
        {
            EmployeeCode = GenerateEmployeeCode()
        }));
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
        return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(ToForm(employee)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, EmployeeFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var employee = _dbContext.Employees.Find(id);
        if (employee == null) return NotFound();

        model.EmployeeCode = employee.EmployeeCode;
        ModelState.Remove(nameof(model.EmployeeCode));
        ValidateUniqueFields(model);
        if (!ModelState.IsValid)
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));

        ApplyForm(employee, model, preserveEmployeeCode: true);
        employee.UpdatedDate = DateTime.UtcNow;

        if (!TrySave("The employee could not be updated because the data conflicts with an existing record."))
            return View("~/Views/Main/AddEmpHrm.cshtml", PopulateManagers(model));

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
        var code = model.EmployeeCode.Trim().ToUpperInvariant();
        var email = model.Email.Trim().ToLowerInvariant();
        var phoneNumber = model.PhoneNumber.Trim();
        if (_dbContext.Employees.Any(employee => employee.Id != model.Id && employee.EmployeeCode == code))
            ModelState.AddModelError(nameof(model.EmployeeCode), "Employee code already exists.");
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
        employee.Address = Clean(model.Address);
        employee.City = Clean(model.City);
        employee.State = Clean(model.State);
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
        Address = employee.Address,
        City = employee.City,
        State = employee.State,
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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
