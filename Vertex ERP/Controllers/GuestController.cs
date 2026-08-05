using Microsoft.AspNetCore.Mvc;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers
{
    public class GuestController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        public GuestController(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(email);
            var user = _dbContext.AppUsers
                .FirstOrDefault(appUser =>
                    appUser.NormalizedUsername == normalizedUsername &&
                    appUser.IsActive);

            if (user != null && PasswordHashService.VerifyPassword(password, user.PasswordHash))
            {
                HttpContext.Session.SetString("email", user.Username);
                HttpContext.Session.SetString("username", user.Username);
                HttpContext.Session.SetString("role", user.Role);
                HttpContext.Session.SetString("fullName", user.FullName);
                return RedirectToAction("Dashboard");
            }

            ViewBag.ErrorMessage = "Invalid username or password";
            return View();
        }

        public IActionResult UserSettings()
        {
            //ViewBag.Users = _dbContext.AppUsers
            //    .OrderBy(user => user.Role)
            //    .ThenBy(user => user.Username)
            //    .ToList();

            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpPost]
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

            var allowedRoles = new[] { "User", "Supervisor", "Admin" };
            user.FullName = fullName.Trim();
            user.Role = allowedRoles.Contains(role) ? role : "User";
            user.IsActive = isActive;

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Employee profile updated successfully.";
            return RedirectToAction("UserSettings");
        }

        public IActionResult Dashboard()
        {
            return View();
        }

        [HttpPost]
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

        public IActionResult Employees()
        {
            return View();
        }

        public IActionResult EmpAddRequirement()
        {
            return View();
        }

        public IActionResult Attendence()
        {
            return View();
        }

        public IActionResult AddAttendance()
        {
            return View();
        }

        public IActionResult Reports()
        {
            return View();
        }

        public IActionResult Hrms()
        {
            return View();
        }

        public IActionResult AddEmpHrm()
        {
            return View();
        }

        public IActionResult TaskMgm()
        {
            return View();
        }

        public IActionResult ProjectMgm()
        {
            return View();
        }

        public IActionResult AddProjectMgm()
        {
            return View();
        }

        public IActionResult DocumentMgm()
        {
            return View();
        }

        public IActionResult AddDocMgmSave()
        {
            return View();
        }

        public IActionResult AdminPanel()
        {
            return View();
        }

        public IActionResult AddAdminPanel()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }

        public IActionResult DepartmentManagement()
        {
            return View();
        }

        public IActionResult LeaveManagement()
        {
            return View();
        }
    }
}
