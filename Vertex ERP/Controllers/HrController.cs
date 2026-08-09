using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace Vertex_ERP.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR")]
    public class HrController : Controller
    {
        private const long MaximumPhotoSize = 5 * 1024 * 1024;
        private readonly ApplicationDbContext _dbContext;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<HrController> _logger;

        public HrController(ApplicationDbContext dbContext, IWebHostEnvironment environment, ILogger<HrController> logger)
        {
            _dbContext = dbContext;
            _environment = environment;
            _logger = logger;
        }

        public IActionResult EmployeeDashboard()
        {
            return RedirectToAction("Index", "Employee");
        }

        public IActionResult AttendanceDashboard()
        {
            return View();
        }

        public IActionResult EmpLeaveManagement()
        {
            return View();
        }

        public IActionResult EmpPayroll()
        {
            return View();
        }

        public IActionResult EmpPerformance()
        {
            return View();
        }

        public IActionResult Recuirement()
        {
            return View();
        }

        public IActionResult EmpDocuments()
            {
                return View();
        }

        public IActionResult Holiday()
        {
            return View();
        }
        public async Task<IActionResult> Department()
        {
            var departments = await _dbContext.Departments.AsNoTracking()
                .OrderBy(department => department.DepartmentName)
                .Select(department => new DepartmentOverviewItem
                {
                    Id = department.Id,
                    Name = department.DepartmentName,
                    Code = department.DepartmentCode,
                    Description = department.Description,
                    EmployeeCount = department.Employees.Count,
                    Status = department.IsActive ? "Active" : "Inactive",
                    ManagerName = department.Manager != null ? department.Manager.FullName : "Not Assigned"
                })
                .ToListAsync();

            return View(new DepartmentOverviewViewModel
            {
                Departments = departments,
                TotalDepartments = departments.Count,
                ActiveDepartments = departments.Count(department => department.Status == "Active"),
                TotalEmployees = await _dbContext.Employees.CountAsync()
            });
        }

        [HttpGet]
        public async Task<IActionResult> AddDepartment()
            => View(await PopulateDepartmentManagersAsync(new AddDepartmentViewModel()));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(AddDepartmentViewModel model)
        {
            var departmentName = model.DepartmentName?.Trim() ?? string.Empty;
            var departmentCode = model.DepartmentCode?.Trim() ?? string.Empty;

            if (await _dbContext.Departments.AnyAsync(department => department.DepartmentName.ToLower() == departmentName.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentName), "Department name already exists.");
            if (await _dbContext.Departments.AnyAsync(department => department.DepartmentCode.ToLower() == departmentCode.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentCode), "Department code already exists.");
            if (model.ManagerId.HasValue && !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ManagerId), "Please select a valid active employee as manager.");

            if (!ModelState.IsValid) return View(await PopulateDepartmentManagersAsync(model));

            _dbContext.Departments.Add(new Department
            {
                DepartmentName = departmentName,
                DepartmentCode = departmentCode,
                Description = Clean(model.Description),
                IsActive = model.IsActive,
                ManagerId = model.ManagerId,
                CreatedDate = DateTime.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync();
                TempData["DepartmentMessage"] = "Department added successfully.";
                return RedirectToAction(nameof(Department));
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Database error while adding department {DepartmentCode}.", departmentCode);
                ModelState.AddModelError(string.Empty, "Unable to add department. The name or code may already exist.");
                return View(await PopulateDepartmentManagersAsync(model));
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditDepartment(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
            if (department == null) return NotFound();
            return View("AddDepartment", await PopulateDepartmentManagersAsync(new AddDepartmentViewModel
            {
                Id = department.Id,
                DepartmentName = department.DepartmentName,
                DepartmentCode = department.DepartmentCode,
                Description = department.Description,
                IsActive = department.IsActive,
                ManagerId = department.ManagerId
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDepartment(int id, AddDepartmentViewModel model)
        {
            if (id != model.Id) return BadRequest();
            var department = await _dbContext.Departments.FirstOrDefaultAsync(item => item.Id == id);
            if (department == null) return NotFound();

            var departmentName = model.DepartmentName?.Trim() ?? string.Empty;
            var departmentCode = model.DepartmentCode?.Trim() ?? string.Empty;
            if (await _dbContext.Departments.AnyAsync(item => item.Id != id && item.DepartmentName.ToLower() == departmentName.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentName), "Department name already exists.");
            if (await _dbContext.Departments.AnyAsync(item => item.Id != id && item.DepartmentCode.ToLower() == departmentCode.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentCode), "Department code already exists.");
            if (model.ManagerId.HasValue && !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ManagerId), "Please select a valid active employee as manager.");

            if (!ModelState.IsValid) return View("AddDepartment", await PopulateDepartmentManagersAsync(model));

            department.DepartmentName = departmentName;
            department.DepartmentCode = departmentCode;
            department.Description = Clean(model.Description);
            department.IsActive = model.IsActive;
            department.ManagerId = model.ManagerId;
            department.UpdatedDate = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            TempData["DepartmentMessage"] = "Department updated successfully.";
            return RedirectToAction(nameof(Department));
        }

        [HttpGet]
        public async Task<IActionResult> DepartmentDetails(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking()
                .Include(item => item.Manager)
                .Include(item => item.Employees)
                .FirstOrDefaultAsync(item => item.Id == id);
            return department == null ? NotFound() : View(department);
        }

        [HttpGet]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking()
                .Include(item => item.Manager)
                .FirstOrDefaultAsync(item => item.Id == id);
            return department == null ? NotFound() : View(department);
        }

        [HttpPost, ActionName("DeleteDepartment")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDepartmentConfirmed(int id)
        {
            var department = await _dbContext.Departments.FirstOrDefaultAsync(item => item.Id == id);
            if (department == null) return NotFound();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var assignedEmployees = await _dbContext.Employees
                .Where(employee => employee.DepartmentId == id)
                .ToListAsync();
            foreach (var employee in assignedEmployees)
            {
                employee.DepartmentId = null;
                employee.Department = "Unassigned";
                employee.UpdatedDate = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
            _dbContext.Departments.Remove(department);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["DepartmentMessage"] = "Department deleted successfully.";
            return RedirectToAction(nameof(Department));
        }

        public IActionResult ExpenseClaim()
        {
            return View();
        }

        public IActionResult AssetManagement()
        {
            return View();
        }

        public IActionResult Meetings()
        {
            return View();
        }

        public IActionResult HrmReports()
        {
            return View();
        }


        [HttpGet]
        public async Task<IActionResult> HrAddEmp()
        {
            return View(await PopulateManagersAsync(new HrAddEmployeeViewModel()));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HrAddEmp(HrAddEmployeeViewModel model)
        {
            var employeeCode = model.EmployeeId.Trim().ToUpperInvariant();
            var email = model.Email.Trim().ToLowerInvariant();
            var loginUsername = model.LoginUsername.Trim();
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(loginUsername);

            if (await _dbContext.Employees.AnyAsync(employee => employee.EmployeeCode == employeeCode))
                ModelState.AddModelError(nameof(model.EmployeeId), "Employee ID already exists.");
            if (await _dbContext.Employees.AnyAsync(employee => employee.Email == email))
                ModelState.AddModelError(nameof(model.Email), "Email address already exists.");
            if (await _dbContext.Employees.AnyAsync(employee => employee.PhoneNumber == model.Phone.Trim()))
                ModelState.AddModelError(nameof(model.Phone), "Mobile Number already exists.");
            if (await _dbContext.AppUsers.AnyAsync(user => user.NormalizedUsername == normalizedUsername))
                ModelState.AddModelError(nameof(model.LoginUsername), "Login username already exists.");
            if (model.ReportingManagerId.HasValue &&
                !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ReportingManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ReportingManagerId), "Please select an active reporting manager.");

            var selectedDepartment = model.DepartmentId.HasValue
                ? await _dbContext.Departments.AsNoTracking().FirstOrDefaultAsync(department => department.Id == model.DepartmentId.Value && department.IsActive)
                : null;
            if (selectedDepartment == null)
                ModelState.AddModelError(nameof(model.DepartmentId), "Please select an active department.");

            var photoExtension = await ValidatePhotoAsync(model.EmployeePhoto);
            if (!ModelState.IsValid)
                return View(await PopulateManagersAsync(model));

            string? photoPath = null;
            try
            {
                if (model.EmployeePhoto != null && photoExtension != null)
                    photoPath = await SavePhotoAsync(model.EmployeePhoto, photoExtension);

                var firstName = model.FirstName.Trim();
                var lastName = Clean(model.LastName);
                var employee = new Employee
                {
                    EmployeeCode = employeeCode,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    Email = email,
                    PhoneNumber = model.Phone.Trim(),
                    DateOfBirth = model.DateOfBirth,
                    Gender = Clean(model.Gender),
                    MaritalStatus = Clean(model.MaritalStatus),
                    EmergencyContact = Clean(model.EmergencyContact),
                    Department = selectedDepartment!.DepartmentName,
                    DepartmentId = selectedDepartment.Id,
                    Designation = model.Designation.Trim(),
                    JoiningDate = model.JoiningDate,
                    EmploymentType = Clean(model.EmploymentType) ?? "Permanent",
                    ReportingManagerId = model.ReportingManagerId,
                    WorkLocation = Clean(model.WorkLocation),
                    Address = Clean(model.Address),
                    City = Clean(model.City),
                    State = Clean(model.State),
                    PinCode = Clean(model.PinCode),
                    PhotoPath = photoPath,
                    EmployeeStatus = "Active",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _dbContext.Employees.Add(employee);
                _dbContext.AppUsers.Add(new AppUser
                {
                    Username = loginUsername,
                    NormalizedUsername = normalizedUsername,
                    PasswordHash = PasswordHashService.HashPassword(model.TemporaryPassword),
                    Role = "Employee",
                    FullName = employee.FullName,
                    IsActive = true,
                    Employee = employee,
                    MustChangePassword = model.MustChangePassword,
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();
                TempData["EmployeeMessage"] = $"Employee and login account '{loginUsername}' added successfully.";
                return RedirectToAction("Index", "Employee");
            }
            catch (DbUpdateException exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "Database error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to add employee. Please try again.");
            }
            catch (IOException exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "File error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to save the employee photo. Please try again.");
            }
            catch (Exception exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "Unexpected error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to add employee. Please try again.");
            }

            return View(await PopulateManagersAsync(model));
        }

        private async Task<HrAddEmployeeViewModel> PopulateManagersAsync(HrAddEmployeeViewModel model)
        {
            model.Managers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .OrderBy(employee => employee.FirstName)
                .ThenBy(employee => employee.LastName)
                .ToListAsync();
            model.Departments = await _dbContext.Departments.AsNoTracking()
                .Where(department => department.IsActive)
                .OrderBy(department => department.DepartmentName)
                .ToListAsync();
            return model;
        }

        private async Task<AddDepartmentViewModel> PopulateDepartmentManagersAsync(AddDepartmentViewModel model)
        {
            model.Managers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .OrderBy(employee => employee.FirstName)
                .ThenBy(employee => employee.LastName)
                .ToListAsync();
            return model;
        }

        private async Task<string?> ValidatePhotoAsync(IFormFile? photo)
        {
            if (photo == null) return null;
            if (photo.Length == 0 || photo.Length > MaximumPhotoSize)
            {
                ModelState.AddModelError(nameof(HrAddEmployeeViewModel.EmployeePhoto), "Photo must be smaller than 5 MB.");
                return null;
            }

            var header = new byte[8];
            await using var stream = photo.OpenReadStream();
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));
            var isJpeg = bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = bytesRead >= 8 && header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            if (isJpeg && photo.ContentType is "image/jpeg" or "image/jpg") return ".jpg";
            if (isPng && photo.ContentType == "image/png") return ".png";

            ModelState.AddModelError(nameof(HrAddEmployeeViewModel.EmployeePhoto), "Please select a valid JPG, JPEG or PNG image.");
            return null;
        }

        private async Task<string> SavePhotoAsync(IFormFile photo, string extension)
        {
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var uploadDirectory = Path.Combine(_environment.WebRootPath, "uploads", "employees");
            Directory.CreateDirectory(uploadDirectory);
            var physicalPath = Path.Combine(uploadDirectory, fileName);
            try
            {
                await using var target = new FileStream(physicalPath, FileMode.CreateNew);
                await photo.CopyToAsync(target);
                return $"/uploads/employees/{fileName}";
            }
            catch
            {
                if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
                throw;
            }
        }

        private void DeletePhotoIfPresent(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            var physicalPath = Path.Combine(_environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    }
}
