using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers
{
    [Authorize]
    public class MainController : Controller
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IAttendanceProcessingService _attendanceProcessingService;

        public MainController(ApplicationDbContext dbContext, IAttendanceProcessingService attendanceProcessingService)
        {
            _dbContext = dbContext;
            _attendanceProcessingService = attendanceProcessingService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Start()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToRoleHome();
            }

            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe = false)
        {
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(email);
            var user = _dbContext.AppUsers
                .FirstOrDefault(appUser =>
                    appUser.NormalizedUsername == normalizedUsername &&
                    appUser.IsActive);

            if (user != null && PasswordHashService.VerifyPassword(password, user.PasswordHash))
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.FullName),
                    new(ClaimTypes.Role, user.Role),
                    new("username", user.Username)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
                    });

                HttpContext.Session.SetString("email", user.Username);
                HttpContext.Session.SetString("username", user.Username);
                HttpContext.Session.SetString("role", user.Role);
                HttpContext.Session.SetString("fullName", user.FullName);
                return RedirectToRoleHome(user.Role);
            }

            ViewBag.ErrorMessage = "Invalid username or password";
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult UserSettings()
        {
            //ViewBag.Users = _dbContext.AppUsers
            //    .OrderBy(user => user.Role)
            //    .ThenBy(user => user.Username)
            //    .ToList();

            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public IActionResult CreateUser(string username, string fullName, string password, string role)
        {
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(username);

            if (_dbContext.AppUsers.Any(user => user.NormalizedUsername == normalizedUsername))
            {
                TempData["UserSettingError"] = "This employee ID / username already exists.";
                return RedirectToAction("UserSettings");
            }

            _dbContext.AppUsers.Add(new AppUser
            {
                Username = username.Trim(),
                NormalizedUsername = normalizedUsername,
                FullName = fullName.Trim(),
                Role = string.IsNullOrWhiteSpace(role) ? "User" : role.Trim(),
                PasswordHash = PasswordHashService.HashPassword(password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Login user created successfully.";
            return RedirectToAction("UserSettings");
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public IActionResult UpdateUser(int id, string fullName, string role, bool isActive)
        {
            var user = _dbContext.AppUsers.FirstOrDefault(appUser => appUser.Id == id);

            if (user == null)
            {
                TempData["UserSettingError"] = "Selected employee login was not found.";
                return RedirectToAction("UserSettings");
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                TempData["UserSettingError"] = "Employee name is required.";
                return RedirectToAction("UserSettings");
            }

            var allowedRoles = new[] { "Employee", "HR", "Admin", "User", "Supervisor" };
            user.FullName = fullName.Trim();
            user.Role = allowedRoles.Contains(role) ? role : "User";
            user.IsActive = isActive;

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Employee profile updated successfully.";
            return RedirectToAction("UserSettings");
        }

        [Authorize(Roles = "Employee,Admin,HR")]
        public IActionResult Dashboard()
        {
            return View();
        }

        [Authorize(Roles = "Employee,User")]
        public IActionResult EmployeeHome()
        {
            return RedirectToAction(nameof(Employees));
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult LocationTracking()
        {
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,HR")]
        public IActionResult AskAssistant([FromBody] AssistantRequest request)
        {
            var message = (request?.Message ?? string.Empty).Trim().ToLowerInvariant();
            var totalUsers = _dbContext.AppUsers.Count(user => user.IsActive);
            var admins = _dbContext.AppUsers.Count(user => user.IsActive && user.Role == "Admin");
            var supervisors = _dbContext.AppUsers.Count(user => user.IsActive && user.Role == "Supervisor");
            var employees = _dbContext.AppUsers.Count(user => user.IsActive && user.Role == "User");

            var reply = "Hello. I am the Vertex AI assistant. You can ask me for today's company report, active login users, attendance summary, or meeting status.";

            if (message.Contains("report") ||
                message.Contains("attendance") ||
                message.Contains("present") ||
                message.Contains("company") ||
                message.Contains("today") ||
                message.Contains("aaj") ||
                message.Contains("batao"))
            {
                reply = "Live attendance and company reporting data is not available yet. " +
                    $"The system currently has {totalUsers} active login users configured: {admins} admin, {supervisors} supervisor, and {employees} employee users.";
            }
            else if (message.Contains("user") ||
                message.Contains("login") ||
                message.Contains("employee"))
            {
                reply = $"The ERP login database currently has {totalUsers} active users. Role split: {admins} admin, {supervisors} supervisor, and {employees} employee users.";
            }
            else if (message.Contains("meeting") ||
                message.Contains("calendar") ||
                message.Contains("google") ||
                message.Contains("baare"))
            {
                reply = "Google Calendar is not connected yet. In the next phase, after OAuth setup, I will be able to read real calendar meetings and summarize them here.";
            }
            else if (message.Contains("hello") ||
                message.Contains("hi") ||
                message.Contains("good morning"))
            {
                reply = "Hello, good morning. The Vertex ERP assistant is ready. You can ask about company reports, attendance, login users, or calendar status.";
            }

            return Json(new { reply });
        }

        [Authorize(Roles = "Employee,User,Admin,HR")]
        public async Task<IActionResult> Employees(int? id)
        {
            Employee? employee;
            if (User.IsInRole("Employee") || User.IsInRole("User"))
            {
                var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var linkedEmployeeId = int.TryParse(userIdText, out var userId)
                    ? await _dbContext.AppUsers.AsNoTracking()
                        .Where(user => user.Id == userId)
                        .Select(user => user.EmployeeId)
                        .FirstOrDefaultAsync()
                    : null;
                var username = (User.FindFirstValue("username") ?? User.Identity?.Name ?? string.Empty).Trim().ToLowerInvariant();
                var fullName = (User.FindFirstValue(ClaimTypes.Name) ?? string.Empty).Trim().ToLowerInvariant();
                employee = await _dbContext.Employees.AsNoTracking()
                    .Include(item => item.ReportingManager)
                    .FirstOrDefaultAsync(item =>
                        (linkedEmployeeId.HasValue && item.Id == linkedEmployeeId.Value) ||
                        item.Email.ToLower() == username ||
                        item.EmployeeCode.ToLower() == username ||
                        item.FullName.ToLower() == fullName);
            }
            else
            {
                employee = id.HasValue
                    ? await _dbContext.Employees.AsNoTracking()
                        .Include(item => item.ReportingManager)
                        .FirstOrDefaultAsync(item => item.Id == id.Value)
                    : await _dbContext.Employees.AsNoTracking()
                        .Include(item => item.ReportingManager)
                        .OrderByDescending(item => item.UpdatedDate ?? item.CreatedDate)
                        .FirstOrDefaultAsync();
            }

            return View(employee == null ? new EmployeeSelfServiceViewModel() : EmployeeSelfServiceViewModel.FromEmployee(employee));
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult EmpAddRequirement()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Attendence(string? searchQuery, string? department, DateOnly? filterDate, string? status, CancellationToken cancellationToken)
        {
            var date = filterDate ?? DateOnly.FromDateTime(DateTime.Today);
            return View(await _attendanceProcessingService.GetDailyAttendanceAsync(date, searchQuery, department, status, cancellationToken));
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddAttendance()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Reports()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Hrms()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddEmpHrm()
        {
            return RedirectToAction("HrAddEmp", "Hr");
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult TaskMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult ProjectMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddProjectMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult DocumentMgm()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddDocMgmSave()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AdminPanel()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddAdminPanel()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Settings()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult DepartmentManagement()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult LeaveManagement()
        {
            return View();
        }

        public IActionResult AccessDenied()
        {
            return User.IsInRole("Employee") || User.IsInRole("User")
                ? RedirectToAction("EmployeeHome", "Main")
                : Forbid();
        }

        private IActionResult RedirectToRoleHome(string? role = null)
        {
            var effectiveRole = role ?? User.FindFirstValue(ClaimTypes.Role);
            return string.Equals(effectiveRole, "Employee", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(effectiveRole, "User", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction("EmployeeHome", "Main")
                : RedirectToAction("Dashboard", "Main");
        }
    }
}



