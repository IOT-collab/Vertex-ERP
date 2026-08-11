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
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.ErrorMessage = "Username and password are required.";
                return View();
            }

            var normalizedUsername = DatabaseInitializer.NormalizeUsername(email);
            var normalizedLogin = email.Trim().ToLowerInvariant();
            var normalizedPassword = password.Trim();

            // Always prefer the login username that HR saved for the employee. Looking up
            // username, employee ID and email in one query can select a different account
            // when one employee's username matches another employee's ID or email.
            var user = await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser => appUser.IsActive && appUser.NormalizedUsername == normalizedUsername);

            // Employee ID and corporate email remain supported as fallbacks, but only when
            // no account exists with the exact username entered on the login form.
            user ??= await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser =>
                    appUser.IsActive && appUser.Employee != null &&
                    (appUser.Employee.EmployeeCode.ToLower() == normalizedLogin ||
                     appUser.Employee.Email.ToLower() == normalizedLogin));

            if (user != null && PasswordHashService.VerifyPassword(normalizedPassword, user.PasswordHash))
            {
                var role = AccountRoleService.Normalize(user.Role);
                if (role == null || ((role == AccountRoleService.Manager || role == AccountRoleService.Employee) && !user.EmployeeId.HasValue))
                {
                    ViewBag.ErrorMessage = "This login account is not correctly linked to an employee role. Please contact HR.";
                    return View();
                }
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.FullName),
                    new(ClaimTypes.Role, role),
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
                HttpContext.Session.SetString("role", role);
                HttpContext.Session.SetString("fullName", user.FullName);
                return RedirectToRoleHome(role);
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

            var allowedRoles = new[] { AccountRoleService.Employee, AccountRoleService.Manager, AccountRoleService.HR, AccountRoleService.Admin };
            user.FullName = fullName.Trim();
            user.Role = allowedRoles.Contains(role) ? role : "User";
            user.IsActive = isActive;

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Employee profile updated successfully.";
            return RedirectToAction("UserSettings");
        }

        [Authorize(Roles = "Employee,Admin,HR")]
        public async Task<IActionResult> Dashboard()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var employees = await _dbContext.Employees.AsNoTracking().ToListAsync();
            var departments = await _dbContext.Departments.AsNoTracking()
                .Where(department => department.IsActive)
                .OrderBy(department => department.DepartmentName)
                .ToListAsync();
            var todayLogs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId != null && log.PunchTime >= today && log.PunchTime < tomorrow)
                .ToListAsync();
            var weekLogs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId != null && log.PunchTime >= weekStart && log.PunchTime < tomorrow)
                .ToListAsync();
            var tasks = await _dbContext.WorkTasks.AsNoTracking().ToListAsync();
            var presentIds = todayLogs.Where(log => log.EmployeeId.HasValue).Select(log => log.EmployeeId!.Value).Distinct().ToHashSet();
            var lateCount = todayLogs.Where(log => log.EmployeeId.HasValue)
                .GroupBy(log => log.EmployeeId!.Value)
                .Count(group => group.Min(log => log.PunchTime).TimeOfDay > new TimeSpan(10, 0, 0));
            var closedStatuses = new[] { "Completed", "Done" };
            var openTasks = tasks.Where(task => !closedStatuses.Contains(task.Status, StringComparer.OrdinalIgnoreCase)).ToList();

            var activity = employees.OrderByDescending(employee => employee.CreatedDate).Take(4)
                .Select(employee => new DashboardActivityItem("Employee added", $"{employee.FullName} · {employee.Department}", employee.CreatedDate))
                .Concat(todayLogs.OrderByDescending(log => log.PunchTime).Take(4)
                    .Select(log => new DashboardActivityItem("Attendance recorded", $"Employee #{log.EmployeeId} · {log.PunchState ?? "Punch"}", log.PunchTime)))
                .OrderByDescending(item => item.OccurredAt).Take(6).ToList();

            var model = new DashboardViewModel
            {
                TotalWorkforce = employees.Count,
                ActiveWorkforce = employees.Count(employee => employee.IsActive),
                PresentToday = presentIds.Count,
                LateToday = lateCount,
                AbsentToday = Math.Max(0, employees.Count(employee => employee.IsActive) - presentIds.Count),
                OpenTasks = openTasks.Count,
                OverdueTasks = openTasks.Count(task => task.DueDate < DateOnly.FromDateTime(today)),
                CompletedTasks = tasks.Count(task => closedStatuses.Contains(task.Status, StringComparer.OrdinalIgnoreCase)),
                RecentEmployees = employees.OrderByDescending(employee => employee.CreatedDate).Take(8)
                    .Select(employee => new DashboardEmployeeRow(employee.Id, employee.EmployeeCode, employee.FullName, employee.Email, employee.Department, employee.Designation, employee.IsActive, employee.PhotoPath)).ToList(),
                Departments = departments.Select(department => new DashboardDepartmentMetric(
                    department.DepartmentName,
                    employees.Count(employee => employee.DepartmentId == department.Id))).ToList(),
                WeeklyAttendance = Enumerable.Range(0, 5).Select(offset => weekStart.AddDays(offset))
                    .Select(day => new DashboardDayMetric(day.ToString("ddd"), weekLogs.Where(log => log.PunchTime.Date == day.Date).Select(log => log.EmployeeId).Distinct().Count())).ToList(),
                RecentActivity = activity
            };
            return View(model);
        }

        [Authorize(Roles = "Employee,User")]
        public IActionResult EmployeeHome()
        {
            return RedirectToAction(nameof(Employees));
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeTasks()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var tasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Manager)
                .Where(task => task.AssigneeId == employee.Id).OrderBy(task => task.DueDate).ToListAsync();
            return View(new EmployeeTasksViewModel { Employee = employee, Tasks = tasks });
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeAttendance(int? month, int? year)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var now = DateTime.Today;
            var requestedMonth = Math.Clamp(month ?? now.Month, 1, 12);
            var requestedYear = Math.Clamp(year ?? now.Year, 2000, now.Year);
            var start = new DateTime(requestedYear, requestedMonth, 1);
            var end = start.AddMonths(1);
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= start && log.PunchTime < end)
                .OrderBy(log => log.PunchTime).Select(log => log.PunchTime).ToListAsync();
            var leaves = await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.EmployeeId == employee.Id && request.Status == "Approved" && request.ToDate >= DateOnly.FromDateTime(start) && request.FromDate < DateOnly.FromDateTime(end)).ToListAsync();
            var lastDay = end.AddDays(-1) < now ? end.AddDays(-1) : now;
            var days = new List<EmployeeAttendanceDay>();
            for (var date = start; date <= lastDay; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var punches = logs.Where(log => log.Date == date.Date).ToList();
                var dateOnly = DateOnly.FromDateTime(date);
                var onLeave = leaves.Any(leave => leave.FromDate <= dateOnly && leave.ToDate >= dateOnly);
                days.Add(new EmployeeAttendanceDay(dateOnly, punches.FirstOrDefault(), punches.Count > 1 ? punches.Last() : null, punches.Count > 0 ? "Present" : onLeave ? "On Leave" : "Absent"));
            }
            return View(new EmployeeAttendanceViewModel { Employee = employee, Month = DateOnly.FromDateTime(start), Days = days.OrderByDescending(day => day.Date).ToList() });
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeLeaves()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var requests = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id).OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            return View(new EmployeeLeaveViewModel { Employee = employee, Requests = requests });
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeProfile()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            return employee == null ? RedirectToAction(nameof(AccessDenied)) : View(employee);
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EditEmployeeProfile()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            return View(await PopulateEmployeeProfileOptionsAsync(new EmployeeProfileEditViewModel
            {
                EmployeeCode = employee.EmployeeCode, FullName = employee.FullName, Email = employee.Email, PhoneNumber = employee.PhoneNumber,
                DateOfBirth = employee.DateOfBirth, Gender = employee.Gender, MaritalStatus = employee.MaritalStatus, EmergencyContact = employee.EmergencyContact ?? string.Empty,
                DepartmentId = employee.DepartmentId, Designation = employee.Designation, ReportingManagerId = employee.ReportingManagerId, JoiningDate = employee.JoiningDate,
                EmploymentType = employee.EmploymentType, WorkLocation = employee.WorkLocation, Address = employee.Address, City = employee.City, State = employee.State, PinCode = employee.PinCode
            }, employee.Id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EditEmployeeProfile(EmployeeProfileEditViewModel model)
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return RedirectToAction(nameof(AccessDenied));
            var employee = await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == employeeId.Value);
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var department = model.DepartmentId.HasValue ? await _dbContext.Departments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == model.DepartmentId && item.IsActive) : null;
            if (department == null) ModelState.AddModelError(nameof(model.DepartmentId), "Please select an active department.");
            if (model.ReportingManagerId == employee.Id) ModelState.AddModelError(nameof(model.ReportingManagerId), "You cannot report to yourself.");
            if (model.ReportingManagerId.HasValue && !await _dbContext.AppUsers.AnyAsync(user => user.EmployeeId == model.ReportingManagerId && user.IsActive && user.Role == "Manager"))
                ModelState.AddModelError(nameof(model.ReportingManagerId), "Please select an active manager.");
            if (!ModelState.IsValid)
            {
                model.EmployeeCode = employee.EmployeeCode;
                model.FullName = employee.FullName;
                model.Email = employee.Email;
                model.PhoneNumber = employee.PhoneNumber;
                model = await PopulateEmployeeProfileOptionsAsync(model, employee.Id);
                return View(model);
            }
            employee.DateOfBirth = model.DateOfBirth;
            employee.Gender = CleanProfileValue(model.Gender);
            employee.MaritalStatus = CleanProfileValue(model.MaritalStatus);
            employee.EmergencyContact = model.EmergencyContact.Trim();
            employee.DepartmentId = department!.Id;
            employee.Department = department.DepartmentName;
            employee.Designation = model.Designation.Trim();
            employee.ReportingManagerId = model.ReportingManagerId;
            employee.JoiningDate = model.JoiningDate;
            employee.EmploymentType = model.EmploymentType.Trim();
            employee.WorkLocation = CleanProfileValue(model.WorkLocation);
            employee.Address = CleanProfileValue(model.Address);
            employee.City = CleanProfileValue(model.City);
            employee.State = CleanProfileValue(model.State);
            employee.PinCode = CleanProfileValue(model.PinCode);
            employee.UpdatedDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            TempData["ProfileMessage"] = "Profile updated successfully. Changes are visible to HR and Admin.";
            return RedirectToAction(nameof(EmployeeProfile));
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeNotifications()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var assignedTasks = await _dbContext.WorkTasks.AsNoTracking().Where(task => task.AssigneeId == employee.Id).ToListAsync();
            var ownLeaves = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id).ToListAsync();
            var taskItems = assignedTasks.Select(task => new EmployeeNotificationItem("Task assigned: " + task.Title, "Status: " + task.Status + " · Due " + task.DueDate.ToString("dd MMM yyyy"), task.CreatedAtUtc, "Task"));
            var leaveItems = ownLeaves.Select(request => new EmployeeNotificationItem("Leave request " + request.Status, request.LeaveType + " · " + request.FromDate.ToString("dd MMM") + " - " + request.ToDate.ToString("dd MMM yyyy"), request.AppliedAtUtc, "Leave"));
            return View(new EmployeeNotificationsViewModel { Employee = employee, Items = taskItems.Concat(leaveItems).OrderByDescending(item => item.CreatedAt).ToList() });
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

        [Authorize(Roles = "Employee,User,Admin,HR,Manager")]
        public async Task<IActionResult> Employees(int? id)
        {
            if (User.IsInRole("Admin") || User.IsInRole("HR") || User.IsInRole("Manager"))
            {
                var employeesQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
                if (User.IsInRole("Manager"))
                {
                    var managerEmployeeId = await GetLoggedInEmployeeIdAsync();
                    var managerDepartmentId = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerEmployeeId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync();
                    employeesQuery = employeesQuery.Where(employee => managerDepartmentId.HasValue && employee.Id != managerEmployeeId && employee.DepartmentId == managerDepartmentId && !_dbContext.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"));
                }
                var employees = await employeesQuery.OrderBy(employee => employee.FullName).ToListAsync();
                var employeeIds = employees.Select(employee => employee.Id).ToList();
                var today = DateTime.Today;
                var presentIds = await _dbContext.AttendanceLogs.AsNoTracking()
                    .Where(log => log.EmployeeId.HasValue && employeeIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < today.AddDays(1))
                    .Select(log => log.EmployeeId!.Value).Distinct().ToListAsync();
                var tasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Assignee)
                    .Where(task => employeeIds.Contains(task.AssigneeId)).OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
                return View("EmployeeOverview", new WorkforceOverviewViewModel { Employees = employees, Tasks = tasks, PresentEmployeeIds = presentIds.ToHashSet() });
            }

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

            if (employee == null) return View("EmployeeDashboard", new EmployeePortalViewModel());
            var employeeTasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Manager)
                .Where(task => task.AssigneeId == employee.Id).OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
            var employeeLeaves = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id)
                .OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            var todayStart = DateTime.Today;
            var punches = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId == employee.Id && log.PunchTime >= todayStart && log.PunchTime < todayStart.AddDays(1))
                .OrderBy(log => log.PunchTime).Select(log => log.PunchTime).ToListAsync();
            return View("EmployeeDashboard", new EmployeePortalViewModel { Employee = employee, Tasks = employeeTasks, LeaveRequests = employeeLeaves, CheckIn = punches.Count > 0 ? punches.First() : null, CheckOut = punches.Count > 1 ? punches.Last() : null });
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

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> Manager()
        {
            var managersQuery = _dbContext.Employees.AsNoTracking()
                .Include(employee => employee.DepartmentEntity)
                .Include(employee => employee.ReportingManager)
                .Where(employee => employee.IsActive && _dbContext.AppUsers
                    .Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"));
            if (User.IsInRole("Manager"))
            {
                var loggedInManagerId = await GetLoggedInEmployeeIdAsync();
                managersQuery = managersQuery.Where(employee => employee.Id == loggedInManagerId);
            }
            var managers = await managersQuery.OrderBy(employee => employee.FirstName).ThenBy(employee => employee.LastName).ToListAsync();

            var managerIds = managers.Select(manager => manager.Id).ToList();
            var managerDepartmentIds = managers.Where(manager => manager.DepartmentId.HasValue).Select(manager => manager.DepartmentId!.Value).Distinct().ToList();
            var teamMembers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && employee.DepartmentId.HasValue && managerDepartmentIds.Contains(employee.DepartmentId.Value) && !managerIds.Contains(employee.Id) && !_dbContext.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"))
                .OrderBy(employee => employee.FullName)
                .ToListAsync();
            var teamMemberIds = teamMembers.Select(employee => employee.Id).ToList();
            var tasks = await _dbContext.WorkTasks.AsNoTracking()
                .Include(task => task.Manager).Include(task => task.Assignee)
                .Where(task => managerIds.Contains(task.ManagerId) || teamMemberIds.Contains(task.AssigneeId))
                .OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
            var leaveRequests = await _dbContext.LeaveRequests.AsNoTracking()
                .Include(request => request.Employee)
                .Where(request => teamMemberIds.Contains(request.EmployeeId))
                .OrderByDescending(request => request.AppliedAtUtc).ToListAsync();

            return View(new ManagerDashboardViewModel
            {
                Managers = managers,
                TeamMembers = teamMembers,
                Tasks = tasks,
                LeaveRequests = leaveRequests
            });
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult AddEmpHrm()
        {
            return RedirectToAction("HrAddEmp", "Hr");
        }

        [Authorize(Roles = "Admin,HR,Manager")]
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

        [Authorize(Roles = "Employee,User,Admin,HR")]
        public IActionResult LeaveManagement()
        {
            return View();
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerAttendance()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var departmentId = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync();
            var team = await _dbContext.Employees.AsNoTracking().Where(employee => departmentId.HasValue && employee.IsActive && employee.Id != managerId && employee.DepartmentId == departmentId && !_dbContext.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager")).OrderBy(employee => employee.FullName).ToListAsync();
            var teamIds = team.Select(employee => employee.Id).ToList();
            var today = DateTime.Today;
            var presentIds = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId.HasValue && teamIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < today.AddDays(1)).Select(log => log.EmployeeId!.Value).Distinct().ToListAsync();
            var leaveIds = await _dbContext.LeaveRequests.AsNoTracking().Where(request => teamIds.Contains(request.EmployeeId) && request.Status == "Approved" && request.FromDate <= DateOnly.FromDateTime(today) && request.ToDate >= DateOnly.FromDateTime(today)).Select(request => request.EmployeeId).Distinct().ToListAsync();
            return View(new ManagerAttendanceViewModel { TeamMembers = team, PresentIds = presentIds.ToHashSet(), OnLeaveIds = leaveIds.ToHashSet() });
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerLeaves()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var departmentId = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerId).Select(employee => employee.DepartmentId).FirstOrDefaultAsync();
            var requests = await _dbContext.LeaveRequests.AsNoTracking().Include(request => request.Employee)
                .Where(request => departmentId.HasValue && request.Employee.DepartmentId == departmentId && request.EmployeeId != managerId).OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            return View(new ManagerSectionViewModel { LeaveRequests = requests });
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerProjects()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var tasks = await _dbContext.WorkTasks.AsNoTracking().Include(task => task.Assignee).Where(task => task.ManagerId == managerId).OrderByDescending(task => task.CreatedAtUtc).ToListAsync();
            return View(new ManagerSectionViewModel { Tasks = tasks });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> ApplyLeave(string leaveType, DateOnly fromDate, DateOnly toDate, string reason)
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var employeeId = int.TryParse(userIdText, out var userId)
                ? await _dbContext.AppUsers.Where(user => user.Id == userId).Select(user => user.EmployeeId).FirstOrDefaultAsync()
                : null;
            if (!employeeId.HasValue)
            {
                TempData["LeaveError"] = "Your login is not linked to an employee profile.";
                return RedirectToAction(nameof(EmployeeLeaves));
            }
            if (string.IsNullOrWhiteSpace(leaveType) || string.IsNullOrWhiteSpace(reason) || fromDate < DateOnly.FromDateTime(DateTime.Today) || toDate < fromDate)
            {
                TempData["LeaveError"] = "Please enter a valid leave type, date range and reason.";
                return RedirectToAction(nameof(EmployeeLeaves));
            }
            _dbContext.LeaveRequests.Add(new LeaveRequest { EmployeeId = employeeId.Value, LeaveType = leaveType.Trim(), FromDate = fromDate, ToDate = toDate, Reason = reason.Trim() });
            await _dbContext.SaveChangesAsync();
            TempData["LeaveMessage"] = "Leave request submitted to your manager.";
            return RedirectToAction(nameof(EmployeeLeaves));
        }

        public IActionResult AccessDenied()
        {
            if (User.IsInRole("Manager"))
                return RedirectToAction("Manager", "Main");
            return User.IsInRole("Employee") || User.IsInRole("User")
                ? RedirectToAction("EmployeeHome", "Main")
                : Forbid();
        }

        private IActionResult RedirectToRoleHome(string? role = null)
        {
            return AccountRoleService.Normalize(role ?? User.FindFirstValue(ClaimTypes.Role)) switch
            {
                AccountRoleService.Manager => RedirectToAction("Manager", "Main"),
                AccountRoleService.Employee => RedirectToAction("EmployeeHome", "Main"),
                AccountRoleService.HR => RedirectToAction("Dashboard", "Main"),
                AccountRoleService.Admin => RedirectToAction("Dashboard", "Main"),
                _ => RedirectToAction("AccessDenied", "Main")
            };
        }

        private async Task<int?> GetLoggedInEmployeeIdAsync()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdText, out var userId)
                ? await _dbContext.AppUsers.AsNoTracking().Where(user => user.Id == userId).Select(user => user.EmployeeId).FirstOrDefaultAsync()
                : null;
        }

        private async Task<Employee?> LoadLoggedInEmployeeAsync()
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            return employeeId.HasValue
                ? await _dbContext.Employees.AsNoTracking().Include(employee => employee.ReportingManager).Include(employee => employee.DepartmentEntity).FirstOrDefaultAsync(employee => employee.Id == employeeId.Value)
                : null;
        }

        private async Task<EmployeeProfileEditViewModel> PopulateEmployeeProfileOptionsAsync(EmployeeProfileEditViewModel model, int employeeId)
        {
            model.Departments = await _dbContext.Departments.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.DepartmentName).ToListAsync();
            model.Managers = await _dbContext.Employees.AsNoTracking().Where(item => item.IsActive && item.Id != employeeId && _dbContext.AppUsers.Any(user => user.EmployeeId == item.Id && user.IsActive && user.Role == "Manager")).OrderBy(item => item.FullName).ToListAsync();
            return model;
        }

        private static string? CleanProfileValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}



