using System.Security.Claims;
using System.Text;
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
        private readonly BankAccountProtectionService _bankProtection;
        private readonly IWebHostEnvironment _environment;
        private readonly IShipmentTrackingService _shipmentTrackingService;

        public MainController(ApplicationDbContext dbContext, IAttendanceProcessingService attendanceProcessingService, BankAccountProtectionService bankProtection, IWebHostEnvironment environment, IShipmentTrackingService shipmentTrackingService)
        {
            _dbContext = dbContext;
            _attendanceProcessingService = attendanceProcessingService;
            _bankProtection = bankProtection;
            _environment = environment;
            _shipmentTrackingService = shipmentTrackingService;
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
            // Password comparison must use the exact value created by HR. Trimming here can
            // silently change an otherwise valid credential.
            var normalizedPassword = password;

            // Always prefer the login username that HR saved for the employee. Looking up
            // username, employee ID and email in one query can select a different account
            // when one employee's username matches another employee's ID or email.
            var user = await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser => appUser.IsActive && appUser.NormalizedUsername == normalizedUsername);

            // Also check by Username (case-insensitive) in case of normalization issues
            user ??= await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser => appUser.IsActive && appUser.Username.ToUpper() == normalizedUsername);

            // Employee ID and corporate email remain supported as fallbacks, but only when
            // no account exists with the exact username entered on the login form.
            user ??= await _dbContext.AppUsers
                .Include(appUser => appUser.Employee)
                .FirstOrDefaultAsync(appUser =>
                    appUser.IsActive && appUser.Employee != null &&
                    (appUser.Employee.EmployeeCode.ToLower() == normalizedLogin ||
                     appUser.Employee.Email.ToLower() == normalizedLogin));

            if (user == null)
            {
                ViewBag.ErrorMessage = "Invalid username or password";
                return View();
            }

            if (!PasswordHashService.VerifyPassword(normalizedPassword, user.PasswordHash))
            {
                ViewBag.ErrorMessage = "Invalid username or password";
                return View();
            }

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

        [HttpGet]
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
            var user = await _dbContext.AppUsers.FirstOrDefaultAsync(item => item.Id == userId && item.IsActive);
            if (user == null) return Forbid();
            if (!PasswordHashService.VerifyPassword(model.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.CurrentPassword), "Current password is incorrect.");
                return View(model);
            }
            if (PasswordHashService.VerifyPassword(model.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.NewPassword), "New password must be different from the current password.");
                return View(model);
            }
            user.PasswordHash = PasswordHashService.HashPassword(model.NewPassword);
            user.MustChangePassword = false;
            await _dbContext.SaveChangesAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            TempData["LoginMessage"] = "Password changed successfully. Please sign in again.";
            return RedirectToAction(nameof(Login));
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> UserSettings()
        {
            ViewBag.Users = await _dbContext.AppUsers.AsNoTracking().OrderBy(user => user.Role).ThenBy(user => user.Username).ToListAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,HR")]
        public IActionResult CreateUser(string username, string fullName, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password) || password.Length < 10)
            { TempData["UserSettingError"] = "Username, name and a password of at least 10 characters are required."; return RedirectToAction("UserSettings"); }
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
                PasswordHash = PasswordHashService.HashPassword(password),
                Role = AccountRoleService.Normalize(role) ?? AccountRoleService.Employee,
                FullName = fullName.Trim(),
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow
            });

            _dbContext.SaveChanges();
            TempData["UserSettingMessage"] = "Login user created successfully.";
            return RedirectToAction("UserSettings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

        [Authorize(Roles = "Admin,HR")]
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
            var taskProgressPercentage = tasks.Count == 0 ? 0 : (int)Math.Round(tasks.Average(task => task.Status switch
            {
                "Completed" or "Done" => 100,
                "In Review" => 75,
                "In Progress" => 40,
                _ => 0
            }));

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
                TaskProgressPercentage = taskProgressPercentage,
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

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeAttendance()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var now = DateTime.Today;
            var start = now.AddDays(-29);
            var end = now.AddDays(1);
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= start && log.PunchTime < end)
                .OrderBy(log => log.PunchTime).Select(log => log.PunchTime).ToListAsync();
            var leaves = await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.EmployeeId == employee.Id && request.Status == "Approved" && request.ToDate >= DateOnly.FromDateTime(start) && request.FromDate < DateOnly.FromDateTime(end)).ToListAsync();
            var lastDay = end.AddDays(-1) < now ? end.AddDays(-1) : now;
            var days = new List<EmployeeAttendanceDay>();
            for (var date = start; date <= lastDay; date = date.AddDays(1))
            {
                var punches = logs.Where(log => log.Date == date.Date).ToList();
                var dateOnly = DateOnly.FromDateTime(date);
                var onLeave = leaves.Any(leave => leave.FromDate <= dateOnly && leave.ToDate >= dateOnly);
                var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                days.Add(new EmployeeAttendanceDay(dateOnly, punches.Count > 0 ? punches.First() : null, punches.Count > 1 ? punches.Last() : null, punches.Count > 0 ? "Present" : onLeave ? "On Leave" : isWeekend ? "Weekend" : "Absent"));
            }
            return View(new EmployeeAttendanceViewModel { Employee = employee, StartDate = DateOnly.FromDateTime(start), EndDate = DateOnly.FromDateTime(now), Days = days.OrderByDescending(day => day.Date).ToList() });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeAssets()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var assets = await _dbContext.EmployeeAssets.AsNoTracking()
                .Where(asset => asset.EmployeeId == employee.Id)
                .OrderByDescending(asset => asset.IssueDate).ThenByDescending(asset => asset.Id).ToListAsync();
            return View(new EmployeeAssetsViewModel { Employee = employee, Assets = assets });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> FieldAttendance()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var today = DateTime.Today;
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode == "Field")
                .OrderBy(log => log.PunchTime)
                .ToListAsync();
            var checkIn = logs.FirstOrDefault(log => log.PunchState == "Check In");
            var checkOut = logs.LastOrDefault(log => log.PunchState == "Check Out");
            return View("FieldAttendanceLive", new FieldAttendanceViewModel
            {
                HasCheckedIn = checkIn != null,
                HasCheckedOut = checkOut != null,
                CheckInTime = checkIn?.PunchTime,
                CheckOutTime = checkOut?.PunchTime,
                CheckInLatitude = checkIn?.Latitude,
                CheckInLongitude = checkIn?.Longitude,
                CheckInAccuracyMetres = checkIn?.AccuracyMetres,
                CheckOutLatitude = checkOut?.Latitude,
                CheckOutLongitude = checkOut?.Longitude,
                CheckOutAccuracyMetres = checkOut?.AccuracyMetres
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> SubmitFieldAttendance([FromBody] FieldAttendanceRequest request)
        {
            if (request.Action is not ("Check In" or "Check Out"))
                return BadRequest(new { message = "Choose Check In or Check Out." });
            if (!request.Latitude.HasValue || !request.Longitude.HasValue || !request.AccuracyMetres.HasValue)
                return BadRequest(new { message = "Your current GPS location is required to mark attendance." });
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            if (request.Latitude.Value == 0 && request.Longitude.Value == 0)
                return BadRequest(new { message = "A valid GPS position is required. Turn on precise location and try again." });
            if (request.AccuracyMetres.Value > 50)
                return BadRequest(new { message = $"GPS accuracy is only ±{request.AccuracyMetres.Value:F0} metres. Attendance requires accuracy within 50 metres. Move outdoors or near a window and try again." });
            if (!request.CapturedAtUtc.HasValue || Math.Abs((DateTimeOffset.UtcNow - request.CapturedAtUtc.Value).TotalMinutes) > 2)
                return BadRequest(new { message = "Your GPS reading is no longer fresh. Capture the current location again." });

            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return Unauthorized();
            var now = DateTime.Now;
            var today = now.Date;
            var existingActions = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employee.Id && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode == "Field")
                .Select(log => log.PunchState)
                .ToListAsync();
            if (existingActions.Contains(request.Action))
                return Conflict(new { message = $"You have already completed {request.Action.ToLowerInvariant()} today." });
            if (request.Action == "Check Out" && !existingActions.Contains("Check In"))
                return BadRequest(new { message = "Please check in before checking out." });

            var device = await _dbContext.BiometricDevices.FirstOrDefaultAsync(item => item.SerialNumber == "FIELD-ATTENDANCE");
            if (device == null)
            {
                device = new BiometricDevice { Name = "ERP Field Attendance", SerialNumber = "FIELD-ATTENDANCE", Model = "Mobile GPS", CommunicationMode = "Field", Notes = "GPS-based employee field attendance." };
                _dbContext.BiometricDevices.Add(device);
                await _dbContext.SaveChangesAsync();
            }

            var selfiePath = await SaveFieldAttendanceSelfieAsync(request.SelfieDataUrl, employee.Id, request.Action);
            var rawPayload = $"FIELD|Action:{request.Action}|Latitude:{request.Latitude:F6}|Longitude:{request.Longitude:F6}|Accuracy:{request.AccuracyMetres:F1}m|Selfie:{selfiePath ?? "Not captured"}";
            _dbContext.AttendanceLogs.Add(new AttendanceLog
            {
                BiometricDeviceId = device.Id,
                EmployeeId = employee.Id,
                DeviceUserId = employee.EmployeeCode,
                PunchTime = now,
                PunchState = request.Action,
                VerificationMode = "GPS Field",
                WorkCode = "Field Attendance",
                UniqueHash = Guid.NewGuid().ToString("N"),
                RawPayload = rawPayload,
                SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                AccuracyMetres = request.AccuracyMetres,
                SelfiePath = selfiePath
            });
            await _dbContext.SaveChangesAsync();
            return Ok(new
            {
                message = $"{request.Action} recorded with GPS at {now:dd MMM yyyy, hh:mm tt}.",
                time = now.ToString("dd MMM yyyy, hh:mm tt"),
                latitude = request.Latitude.Value.ToString("F6"),
                longitude = request.Longitude.Value.ToString("F6"),
                accuracyMetres = request.AccuracyMetres.Value.ToString("F0"),
                selfiePath
            });
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeLeaves()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var requests = await _dbContext.LeaveRequests.AsNoTracking().Where(request => request.EmployeeId == employee.Id).OrderByDescending(request => request.AppliedAtUtc).ToListAsync();
            return View(new EmployeeLeaveViewModel { Employee = employee, Requests = requests });
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EmployeeProfile()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            ViewBag.BankDetail = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id);
            return View(employee);
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User,Manager,HR")]
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
        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> EditEmployeeProfile(EmployeeProfileEditViewModel model)
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return RedirectToAction(nameof(AccessDenied));
            var employee = await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == employeeId.Value);
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
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
            var ownTickets = await _dbContext.QueryTickets.AsNoTracking().Where(ticket => ticket.EmployeeId == employee.Id).ToListAsync();
            var ticketItems = ownTickets.Select(ticket => new EmployeeNotificationItem("Query: " + ticket.Subject, "Status: " + ticket.Status + (string.IsNullOrWhiteSpace(ticket.Resolution) ? string.Empty : " · " + ticket.Resolution), ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc, "Query"));
            return View(new EmployeeNotificationsViewModel { Employee = employee, Items = taskItems.Concat(leaveItems).Concat(ticketItems).OrderByDescending(item => item.CreatedAt).ToList() });
        }

        [Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> EmployeeQueries()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Where(ticket => ticket.EmployeeId == employee.Id).OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            return View(new EmployeeQueryViewModel { Employee = employee, Tickets = tickets });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User")]
        public async Task<IActionResult> RaiseQuery(string subject, string category, string description)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
            {
                TempData["QueryError"] = "Subject and query details are required.";
                return RedirectToAction(nameof(EmployeeQueries));
            }
            _dbContext.QueryTickets.Add(new QueryTicket { EmployeeId = employee.Id, ReportingManagerId = employee.ReportingManagerId, Subject = subject.Trim(), Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(), Description = description.Trim() });
            await _dbContext.SaveChangesAsync();
            TempData["QueryMessage"] = "Your query was sent to your reporting manager and HR.";
            return RedirectToAction(nameof(EmployeeQueries));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> LocationTracking(DateOnly? date, int? employeeId)
        {
            var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
            var start = selectedDate.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            var employeesQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
            var hasCompanyWideAccess = User.IsInRole("Admin") || User.IsInRole("HR");
            var isManagerOnlyView = User.IsInRole("Manager") && !hasCompanyWideAccess;
            if (isManagerOnlyView)
            {
                var managerEmployeeId = await GetLoggedInEmployeeIdAsync();
                if (!managerEmployeeId.HasValue) return Forbid();
                // A manager can only query employees whose Reporting Manager is
                // directly assigned to this manager.
                // The employeeId query-string is applied after this server-side scope.
                employeesQuery = employeesQuery.Where(employee => employee.ReportingManagerId == managerEmployeeId.Value);
            }

            var employees = await employeesQuery
                .OrderBy(employee => employee.FullName)
                .Select(employee => new { employee.Id, employee.FullName, employee.EmployeeCode, employee.Department, employee.Designation })
                .ToListAsync();
            var visibleEmployeeIds = employees.Select(employee => employee.Id).ToList();
            var logs = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue && visibleEmployeeIds.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= start && log.PunchTime < end
                    && log.BiometricDevice.CommunicationMode == "Field")
                .OrderBy(log => log.PunchTime).ToListAsync();
            var items = employees.Select(employee =>
            {
                var employeeLogs = logs.Where(log => log.EmployeeId == employee.Id).ToList();
                var checkIn = employeeLogs.FirstOrDefault(log => log.PunchState == "Check In");
                var checkOut = employeeLogs.LastOrDefault(log => log.PunchState == "Check Out");
                var last = checkOut ?? employeeLogs.LastOrDefault();
                return new SiteEmployeeLocationItem
                {
                    EmployeeId = employee.Id, EmployeeName = employee.FullName, EmployeeCode = employee.EmployeeCode,
                    Department = employee.Department, Designation = employee.Designation,
                    CheckInTime = checkIn?.PunchTime, CheckInLatitude = checkIn?.Latitude, CheckInLongitude = checkIn?.Longitude,
                    CheckInAccuracyMetres = checkIn?.AccuracyMetres, CheckInSelfiePath = checkIn?.SelfiePath,
                    CheckOutTime = checkOut?.PunchTime, CheckOutLatitude = checkOut?.Latitude, CheckOutLongitude = checkOut?.Longitude,
                    CheckOutAccuracyMetres = checkOut?.AccuracyMetres, CheckOutSelfiePath = checkOut?.SelfiePath,
                    LastLocationTime = last?.PunchTime, LastLatitude = last?.Latitude, LastLongitude = last?.Longitude
                };
            }).ToList();
            var selected = items.FirstOrDefault(item => item.EmployeeId == employeeId) ?? items.FirstOrDefault(item => item.CheckInTime.HasValue);
            return View("LocationTrackingLive", new SiteEmployeeLocationViewModel
            {
                Employees = items,
                SelectedEmployee = selected,
                IsManagerView = isManagerOnlyView
            });
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
            var punches = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId == employee.Id
                    && log.PunchTime >= todayStart && log.PunchTime < todayStart.AddDays(1)
                    && log.BiometricDevice.CommunicationMode != "Field")
                .OrderBy(log => log.PunchTime).Select(log => log.PunchTime).ToListAsync();
            return View("EmployeeDashboard", new EmployeePortalViewModel { Employee = employee, Tasks = employeeTasks, LeaveRequests = employeeLeaves, CheckIn = punches.Count > 0 ? punches.First() : null, CheckOut = punches.Count > 1 ? punches.Last() : null });
        }

        [HttpGet]
        [Authorize(Roles = "Employee,User")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> MyLiveAttendance(CancellationToken cancellationToken)
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return Forbid();

            var today = DateTime.Today;
            var punches = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId == employeeId.Value
                    && log.PunchTime >= today && log.PunchTime < today.AddDays(1)
                    && log.BiometricDevice.CommunicationMode != "Field")
                .OrderBy(log => log.PunchTime)
                .Select(log => log.PunchTime)
                .ToListAsync(cancellationToken);

            var checkIn = punches.FirstOrDefault();
            var checkOut = punches.Count > 1 ? punches.Last() : (DateTime?)null;
            return Ok(new
            {
                checkIn = punches.Count > 0 ? checkIn.ToString("hh:mm tt") : null,
                checkOut = checkOut?.ToString("hh:mm tt"),
                status = punches.Count > 0 ? "Present" : "Absent"
            });
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
        public async Task<IActionResult> ExportAttendance(string? searchQuery, string? department, DateOnly? filterDate, string? status, string exportPeriod = "day", string? exportMonth = null, CancellationToken cancellationToken = default)
        {
            var date = filterDate ?? DateOnly.FromDateTime(DateTime.Today);
            var isMonthlyExport = string.Equals(exportPeriod, "month", StringComparison.OrdinalIgnoreCase);
            var dates = new List<DateOnly>();
            if (isMonthlyExport)
            {
                if (!DateOnly.TryParseExact($"{exportMonth}-01", "yyyy-MM-dd", out var monthStart)) return BadRequest("Select a valid month.");
                if (monthStart > DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Future month attendance cannot be exported.");
                var lastDay = monthStart.AddMonths(1).AddDays(-1);
                if (lastDay > DateOnly.FromDateTime(DateTime.Today)) lastDay = DateOnly.FromDateTime(DateTime.Today);
                for (var day = monthStart; day <= lastDay; day = day.AddDays(1)) dates.Add(day);
            }
            else
            {
                if (date > DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Future attendance cannot be exported.");
                dates.Add(date);
            }
            var csv = new StringBuilder();
            var exportRecords = new List<DailyAttendanceViewModel>();
            foreach (var exportDate in dates)
            {
                var attendance = await _attendanceProcessingService.GetDailyAttendanceAsync(exportDate,
                    isMonthlyExport ? null : searchQuery, isMonthlyExport ? null : department, isMonthlyExport ? null : status, cancellationToken);
                exportRecords.AddRange(attendance.Records.Where(item => item.EmployeeId > 0));
            }
            var monthlySummary = exportRecords.GroupBy(item => item.EmployeeId).ToDictionary(group => group.Key, group => new
            {
                Present = group.Count(item => item.Status is "Present" or "Late"),
                Absent = group.Count(item => item.Status == "Absent"),
                Late = group.Count(item => item.Status == "Late"),
                Work = TimeSpan.FromTicks(group.Sum(item => item.WorkingHours.Ticks))
            });
            if (isMonthlyExport)
                return BuildMonthlyAttendanceWorkbook(exportRecords, dates, monthlySummary.ToDictionary(x => x.Key, x => (x.Value.Present, x.Value.Absent, x.Value.Late, x.Value.Work)));
            csv.AppendLine("Emp ID,Employee Name,Department,Date,Day,Check In,Check Out,Working Hours,Punch Count,Status,Month Present Days,Month Absent Days,Month Late Days,Month Total Hours");
            foreach (var item in exportRecords.OrderBy(item => item.EmployeeName).ThenBy(item => item.EmpId).ThenBy(item => item.Date))
            {
                var summary = monthlySummary[item.EmployeeId];
                csv.AppendLine(string.Join(',', new[]
                {
                    Csv(item.EmpId), Csv(item.EmployeeName), Csv(item.Department), Csv(item.Date.ToString("dd-MMM-yyyy")), Csv(item.Date.DayOfWeek.ToString()),
                    Csv(item.CheckIn?.ToString("hh:mm tt") ?? string.Empty), Csv(item.CheckOut?.ToString("hh:mm tt") ?? string.Empty),
                    Csv(item.WorkingHours.ToString(@"hh\:mm")), item.PunchCount.ToString(), Csv(item.Status),
                    summary.Present.ToString(), summary.Absent.ToString(), summary.Late.ToString(), Csv($"{(int)summary.Work.TotalHours:D2}:{summary.Work.Minutes:D2}")
                }));
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
            var periodLabel = dates.Count == 1 ? dates[0].ToString("yyyy-MM-dd") : dates[0].ToString("yyyy-MM");
            return File(bytes, "text/csv; charset=utf-8", $"Attendance-{periodLabel}.csv");
        }

        private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        private FileContentResult BuildMonthlyAttendanceWorkbook(List<DailyAttendanceViewModel> records, List<DateOnly> dates, Dictionary<int, (int Present, int Absent, int Late, TimeSpan Work)> summaries)
        {
            static string X(string? value) => System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
            var month = dates[0];
            var xml = new StringBuilder("<?xml version=\"1.0\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
            xml.Append("<Styles><Style ss:ID=\"Default\"><Alignment ss:Vertical=\"Center\"/><Font ss:FontName=\"Calibri\" ss:Size=\"10\"/></Style><Style ss:ID=\"Title\"><Alignment ss:Horizontal=\"Center\"/><Font ss:Bold=\"1\" ss:Size=\"16\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#1D4ED8\" ss:Pattern=\"Solid\"/></Style><Style ss:ID=\"Header\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#1E3A5F\" ss:Pattern=\"Solid\"/></Style><Style ss:ID=\"Text\"><Borders><Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#DCE3EA\"/></Borders></Style><Style ss:ID=\"Present\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Interior ss:Color=\"#DCFCE7\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#166534\"/></Style><Style ss:ID=\"Late\"><Alignment ss:Horizontal=\"Center\" ss:WrapText=\"1\"/><Interior ss:Color=\"#FEF3C7\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#92400E\"/></Style><Style ss:ID=\"Absent\"><Alignment ss:Horizontal=\"Center\"/><Interior ss:Color=\"#FEE2E2\" ss:Pattern=\"Solid\"/><Font ss:Color=\"#991B1B\"/></Style></Styles>");
            xml.Append($"<Worksheet ss:Name=\"{X(month.ToString("MMM-yyyy"))}\"><Table><Column ss:Width=\"65\"/><Column ss:Width=\"145\"/><Column ss:Width=\"100\"/><Column ss:Width=\"55\" ss:Span=\"3\"/><Column ss:Width=\"70\"/>");
            foreach (var _ in dates) xml.Append("<Column ss:Width=\"82\"/>");
            var columnCount = 7 + dates.Count;
            xml.Append($"<Row ss:Height=\"30\"><Cell ss:StyleID=\"Title\" ss:MergeAcross=\"{columnCount - 1}\"><Data ss:Type=\"String\">Monthly Attendance Pivot Report - {X(month.ToString("MMMM yyyy"))}</Data></Cell></Row>");
            xml.Append("<Row ss:StyleID=\"Header\"><Cell><Data ss:Type=\"String\">Emp ID</Data></Cell><Cell><Data ss:Type=\"String\">Employee Name</Data></Cell><Cell><Data ss:Type=\"String\">Department</Data></Cell><Cell><Data ss:Type=\"String\">Present</Data></Cell><Cell><Data ss:Type=\"String\">Absent</Data></Cell><Cell><Data ss:Type=\"String\">Late</Data></Cell><Cell><Data ss:Type=\"String\">Total Hours</Data></Cell>");
            foreach (var day in dates) xml.Append($"<Cell><Data ss:Type=\"String\">{day:dd MMM}\n{day:ddd}</Data></Cell>");
            xml.Append("</Row>");
            foreach (var employee in records.GroupBy(x => x.EmployeeId).OrderBy(x => x.First().EmployeeName))
            {
                var first = employee.First(); var summary = summaries[employee.Key]; var byDate = employee.ToDictionary(x => x.Date);
                xml.Append($"<Row ss:Height=\"34\"><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.EmpId)}</Data></Cell><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.EmployeeName)}</Data></Cell><Cell ss:StyleID=\"Text\"><Data ss:Type=\"String\">{X(first.Department)}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Present}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Absent}</Data></Cell><Cell><Data ss:Type=\"Number\">{summary.Late}</Data></Cell><Cell><Data ss:Type=\"String\">{(int)summary.Work.TotalHours:D2}:{summary.Work.Minutes:D2}</Data></Cell>");
                foreach (var day in dates)
                {
                    var item = byDate[day]; var style = item.Status is "Present" ? "Present" : item.Status is "Late" ? "Late" : "Absent";
                    var code = item.Status is "Present" ? "P" : item.Status is "Late" ? "L" : "A";
                    var timing = item.CheckIn.HasValue ? $"&#10;{item.CheckIn:hh:mm tt}-{(item.CheckOut.HasValue ? item.CheckOut.Value.ToString("hh:mm tt") : "—")}" : string.Empty;
                    xml.Append($"<Cell ss:StyleID=\"{style}\"><Data ss:Type=\"String\">{code}{timing}</Data></Cell>");
                }
                xml.Append("</Row>");
            }
            xml.Append("</Table><WorksheetOptions xmlns=\"urn:schemas-microsoft-com:office:excel\"><FreezePanes/><FrozenNoSplit/><SplitHorizontal>2</SplitHorizontal><TopRowBottomPane>2</TopRowBottomPane><SplitVertical>3</SplitVertical><LeftColumnRightPane>3</LeftColumnRightPane><ProtectObjects>False</ProtectObjects><ProtectScenarios>False</ProtectScenarios></WorksheetOptions></Worksheet></Workbook>");
            return File(new UTF8Encoding(true).GetBytes(xml.ToString()), "application/vnd.ms-excel", $"Attendance-Pivot-{month:yyyy-MM}.xls");
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> AddAttendance()
        {
            return View(await BuildManualAttendanceViewModelAsync(new ManualAttendanceViewModel()));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> AddAttendance(ManualAttendanceViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Department)) ModelState.AddModelError(nameof(model.Department), "Select a department.");
            if (!model.EmployeeId.HasValue) ModelState.AddModelError(nameof(model.EmployeeId), "Select an employee.");
            if (!model.CheckInTime.HasValue) ModelState.AddModelError(nameof(model.CheckInTime), "Enter check-in time.");
            if (model.CheckOutTime.HasValue && model.CheckInTime.HasValue && model.CheckOutTime <= model.CheckInTime)
                ModelState.AddModelError(nameof(model.CheckOutTime), "Check-out time must be after check-in time.");
            if (model.AttendanceDate > DateOnly.FromDateTime(DateTime.Today))
                ModelState.AddModelError(nameof(model.AttendanceDate), "Future attendance cannot be marked.");

            var employee = model.EmployeeId.HasValue
                ? await _dbContext.Employees.FirstOrDefaultAsync(item => item.Id == model.EmployeeId && item.IsActive)
                : null;
            if (employee == null || !string.Equals(employee.Department, model.Department, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(model.EmployeeId), "The selected employee does not belong to this department.");

            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                var managerDepartment = await _dbContext.Employees.AsNoTracking()
                    .Where(item => item.Id == managerId).Select(item => item.Department).FirstOrDefaultAsync();
                if (employee == null || string.IsNullOrWhiteSpace(managerDepartment)
                    || !string.Equals(employee.Department, managerDepartment, StringComparison.OrdinalIgnoreCase))
                    return Forbid();
            }

            if (employee != null && model.CheckInTime.HasValue)
            {
                var dayStart = model.AttendanceDate.ToDateTime(TimeOnly.MinValue);
                var dayEnd = dayStart.AddDays(1);
                if (await _dbContext.AttendanceLogs.AnyAsync(log => log.EmployeeId == employee.Id && log.PunchTime >= dayStart && log.PunchTime < dayEnd))
                    ModelState.AddModelError(string.Empty, "Attendance already exists for this employee on the selected date.");
            }

            if (!ModelState.IsValid) return View(await BuildManualAttendanceViewModelAsync(model));

            var device = await _dbContext.BiometricDevices.FirstOrDefaultAsync(item => item.SerialNumber == "MANUAL-ENTRY");
            if (device == null)
            {
                device = new BiometricDevice { Name = "ERP Manual Attendance", SerialNumber = "MANUAL-ENTRY", Model = "ERP", CommunicationMode = "Manual", Notes = "System device used for approved manual attendance entries." };
                _dbContext.BiometricDevices.Add(device);
                await _dbContext.SaveChangesAsync();
            }

            var actorUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActorId) ? parsedActorId : 0;
            var actor = await _dbContext.AppUsers.AsNoTracking().Where(user => user.Id == actorUserId).Select(user => user.FullName).FirstOrDefaultAsync()
                ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "Authorized user";
            var checkIn = model.AttendanceDate.ToDateTime(model.CheckInTime!.Value);
            var raw = $"MANUAL|Employee:{employee!.EmployeeCode}|Department:{employee.Department}|MarkedBy:{actor}|Remarks:{CleanProfileValue(model.Remarks)}";
            var logs = new List<AttendanceLog>
            {
                new() { BiometricDeviceId = device.Id, EmployeeId = employee.Id, DeviceUserId = employee.EmployeeCode, PunchTime = checkIn, PunchState = "Check In", VerificationMode = "Manual Approved", UniqueHash = Guid.NewGuid().ToString("N"), RawPayload = raw }
            };
            if (model.CheckOutTime.HasValue)
                logs.Add(new AttendanceLog { BiometricDeviceId = device.Id, EmployeeId = employee.Id, DeviceUserId = employee.EmployeeCode, PunchTime = model.AttendanceDate.ToDateTime(model.CheckOutTime.Value), PunchState = "Check Out", VerificationMode = "Manual Approved", UniqueHash = Guid.NewGuid().ToString("N"), RawPayload = raw });
            _dbContext.AttendanceLogs.AddRange(logs);
            await _dbContext.SaveChangesAsync();
            TempData["AttendanceMessage"] = $"Attendance marked successfully for {employee.FullName}.";
            return RedirectToAction(nameof(Attendence), new { filterDate = model.AttendanceDate.ToString("yyyy-MM-dd"), searchQuery = employee.EmployeeCode });
        }

        private async Task<ManualAttendanceViewModel> BuildManualAttendanceViewModelAsync(ManualAttendanceViewModel model)
        {
            var employeeQuery = _dbContext.Employees.AsNoTracking().Where(employee => employee.IsActive);
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                var managerDepartment = await _dbContext.Employees.AsNoTracking().Where(employee => employee.Id == managerId).Select(employee => employee.Department).FirstOrDefaultAsync();
                employeeQuery = employeeQuery.Where(employee => employee.Id != managerId && employee.Department == managerDepartment);
                model.IsManagerView = true;
            }
            model.Employees = await employeeQuery.OrderBy(employee => employee.FullName).ToListAsync();
            model.Departments = model.Employees.Select(employee => employee.Department).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            return model;
        }

        [Authorize(Roles = "Admin,HR")]
        public IActionResult Reports()
        {
            return View();
        }

        [Authorize(Roles = "Admin,HR")]
        public async Task<IActionResult> Hrms()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var tomorrow = DateTime.Today.AddDays(1);
            var activeEmployeeIds = _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .Select(employee => employee.Id);

            var activeEmployeeIdList = await activeEmployeeIds.ToListAsync();
            var presentEmployeeIds = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue
                    && activeEmployeeIdList.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= DateTime.Today
                    && log.PunchTime < tomorrow)
                .Select(log => log.EmployeeId!.Value)
                .Distinct()
                .ToListAsync();
            var onLeaveEmployeeIds = await _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved"
                    && activeEmployeeIdList.Contains(request.EmployeeId)
                    && request.FromDate <= today
                    && request.ToDate >= today)
                .Select(request => request.EmployeeId)
                .Distinct()
                .ToListAsync();

            var model = new HrmsDashboardViewModel
            {
                TotalEmployees = activeEmployeeIdList.Count,
                PresentToday = presentEmployeeIds.Count,
                OnLeaveToday = onLeaveEmployeeIds.Count,
                AbsentToday = activeEmployeeIdList.Except(presentEmployeeIds).Except(onLeaveEmployeeIds).Count()
            };

            return View(model);
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
            var managerLogin = User.IsInRole("Manager");
            var teamMembers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && !managerIds.Contains(employee.Id)
                    && (managerLogin
                        ? employee.ReportingManagerId.HasValue && managerIds.Contains(employee.ReportingManagerId.Value)
                        : employee.DepartmentId.HasValue && managerDepartmentIds.Contains(employee.DepartmentId.Value))
                    && !_dbContext.AppUsers.Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"))
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
            var queryTickets = await _dbContext.QueryTickets.AsNoTracking().Include(ticket => ticket.Employee)
                .Where(ticket => managerIds.Contains(ticket.ReportingManagerId ?? 0)).OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            var expenseClaims = await _dbContext.ExpenseClaims.AsNoTracking().Include(claim => claim.Employee)
                .Where(claim => claim.Status == "Pending" && (managerIds.Contains(claim.ReportingManagerId ?? 0) || (!managerLogin && claim.RequiresHrApproval)))
                .OrderByDescending(claim => claim.SubmittedAtUtc).ToListAsync();
            var notifications = new List<DashboardNotification>();
            notifications.AddRange(leaveRequests.Where(x => x.Status == "Pending").Select(x => new DashboardNotification(
                "Leave", $"Leave request from {x.Employee.FullName}", $"{x.LeaveType}: {x.FromDate:dd MMM} - {x.ToDate:dd MMM}", managerLogin ? "/Main/ManagerLeaves" : "/Main/WorkflowManagement", x.AppliedAtUtc)));
            notifications.AddRange(queryTickets.Where(x => x.Status != "Resolved" && x.Status != "Closed").Select(x => new DashboardNotification(
                "Query", $"Query from {x.Employee.FullName}", x.Subject, managerLogin ? "/Main/ManagerQueries" : "/Main/WorkflowManagement", x.CreatedAtUtc)));
            notifications.AddRange(expenseClaims.Select(x => new DashboardNotification(
                "Expense", $"Expense claim from {x.Employee.FullName}", $"{x.Title} · ₹{x.Amount:N2}", "/Expense/Index", x.SubmittedAtUtc)));
            notifications.AddRange(tasks.Where(x => !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && x.DueDate < DateOnly.FromDateTime(DateTime.Today)).Select(x => new DashboardNotification(
                "Task", $"Overdue task: {x.Title}", $"Assigned to {x.Assignee.FullName} · due {x.DueDate:dd MMM}", "/Main/TaskMgm", x.CreatedAtUtc)));

            return View(new ManagerDashboardViewModel
            {
                Managers = managers,
                TeamMembers = teamMembers,
                Tasks = tasks,
                LeaveRequests = leaveRequests,
                QueryTickets = queryTickets,
                ExpenseClaims = expenseClaims,
                Notifications = notifications.OrderByDescending(x => x.CreatedAtUtc).ToList()
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

        [HttpGet]
        [Authorize(Roles = "Admin,HR,Manager")]
        public IActionResult OrderTracking()
        {
            return View(new ShipmentTrackingPageViewModel { IsApiConfigured = _shipmentTrackingService.IsConfigured });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> OrderTracking(ShipmentTrackingPageViewModel model, CancellationToken cancellationToken)
        {
            model.IsApiConfigured = _shipmentTrackingService.IsConfigured;
            if (!ModelState.IsValid) return View(model);
            if (!model.IsApiConfigured)
            {
                // The page already shows the configuration notice. Avoid a second,
                // duplicate service error when credentials have not been installed.
                return View(model);
            }
            var result = await _shipmentTrackingService.TrackAsync(model.TrackingNumber, cancellationToken);
            model.Shipment = result.Shipment;
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerAttendance()
        {
            return View(await BuildManagerAttendanceAsync());
        }

        [HttpGet]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ManagerAttendanceLive()
        {
            var model = await BuildManagerAttendanceAsync();
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var teamIds = model.TeamMembers.Select(employee => employee.Id).ToList();
            var lastPunches = await _dbContext.AttendanceLogs.AsNoTracking()
                .Where(log => log.EmployeeId.HasValue && teamIds.Contains(log.EmployeeId.Value)
                    && log.PunchTime >= today && log.PunchTime < tomorrow)
                .GroupBy(log => log.EmployeeId!.Value)
                .Select(group => new { EmployeeId = group.Key, LastPunch = group.Max(log => log.PunchTime) })
                .ToDictionaryAsync(item => item.EmployeeId, item => item.LastPunch);

            return Json(new
            {
                refreshedAt = DateTime.Now.ToString("hh:mm:ss tt"),
                teamMembers = model.TeamMembers.Select(employee => new
                {
                    employee.Id,
                    employee.FullName,
                    employee.Email,
                    employee.EmployeeCode,
                    employee.Department,
                    employee.Designation,
                    status = model.OnLeaveIds.Contains(employee.Id) ? "On Leave" : model.PresentIds.Contains(employee.Id) ? "Present" : "Absent",
                    lastPunch = lastPunches.TryGetValue(employee.Id, out var punch) ? punch.ToString("hh:mm tt") : null
                }),
                teamMembersCount = model.TeamMembers.Count,
                present = model.Present,
                absent = model.Absent,
                onLeave = model.OnLeave
            });
        }

        [HttpGet, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> ManagerNotificationCount(DateTime sinceUtc)
        {
            if (sinceUtc == default) sinceUtc = DateTime.UtcNow;
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                if (!managerId.HasValue) return Json(new { count = 0 });
                var newLeave = await _dbContext.LeaveRequests.AnyAsync(x => x.Status == "Pending" && x.AssignedApproverEmployeeId == managerId && x.AppliedAtUtc > sinceUtc);
                var newQuery = await _dbContext.QueryTickets.AnyAsync(x => x.ReportingManagerId == managerId && x.Status != "Resolved" && x.Status != "Closed" && x.CreatedAtUtc > sinceUtc);
                var newExpense = await _dbContext.ExpenseClaims.AnyAsync(x => !x.RequiresHrApproval && x.ReportingManagerId == managerId && x.Status == "Pending" && x.SubmittedAtUtc > sinceUtc);
                return Json(new { hasNew = newLeave || newQuery || newExpense });
            }
            var hrNewLeave = await _dbContext.LeaveRequests.AnyAsync(x => x.Status == "Pending" && x.AppliedAtUtc > sinceUtc);
            var hrNewQuery = await _dbContext.QueryTickets.AnyAsync(x => x.Status != "Resolved" && x.Status != "Closed" && x.CreatedAtUtc > sinceUtc);
            var hrNewExpense = await _dbContext.ExpenseClaims.AnyAsync(x => x.Status == "Pending" && x.SubmittedAtUtc > sinceUtc);
            return Json(new { hasNew = hrNewLeave || hrNewQuery || hrNewExpense });
        }

        [HttpGet, Authorize(Roles = "Admin,HR,Manager")]
        public async Task<IActionResult> ManagerNotificationCount()
        {
            if (User.IsInRole("Manager"))
            {
                var managerId = await GetLoggedInEmployeeIdAsync();
                if (!managerId.HasValue) return Json(new { count = 0 });
                var leaveCount = await _dbContext.LeaveRequests.CountAsync(x => x.Status == "Pending" && x.AssignedApproverEmployeeId == managerId);
                var queryCount = await _dbContext.QueryTickets.CountAsync(x => x.ReportingManagerId == managerId && x.Status != "Resolved" && x.Status != "Closed");
                var expenseCount = await _dbContext.ExpenseClaims.CountAsync(x => !x.RequiresHrApproval && x.ReportingManagerId == managerId && x.Status == "Pending");
                var overdueCount = await _dbContext.WorkTasks.CountAsync(x => x.ManagerId == managerId && x.Status != "Completed" && x.DueDate < DateOnly.FromDateTime(DateTime.Today));
                return Json(new { count = leaveCount + queryCount + expenseCount + overdueCount });
            }
            var hrLeaveCount = await _dbContext.LeaveRequests.CountAsync(x => x.Status == "Pending");
            var hrQueryCount = await _dbContext.QueryTickets.CountAsync(x => x.Status != "Resolved" && x.Status != "Closed");
            var hrExpenseCount = await _dbContext.ExpenseClaims.CountAsync(x => x.Status == "Pending");
            var hrOverdueCount = await _dbContext.WorkTasks.CountAsync(x => x.Status != "Completed" && x.DueDate < DateOnly.FromDateTime(DateTime.Today));
            return Json(new { count = hrLeaveCount + hrQueryCount + hrExpenseCount + hrOverdueCount });
        }

        private async Task<ManagerAttendanceViewModel> BuildManagerAttendanceAsync()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (!managerId.HasValue) return new ManagerAttendanceViewModel();
            var team = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && employee.ReportingManagerId == managerId.Value)
                .OrderBy(employee => employee.FullName).ToListAsync();
            var teamIds = team.Select(employee => employee.Id).ToList();
            var today = DateTime.Today;
            var presentIds = await _dbContext.AttendanceLogs.AsNoTracking().Where(log => log.EmployeeId.HasValue && teamIds.Contains(log.EmployeeId.Value) && log.PunchTime >= today && log.PunchTime < today.AddDays(1)).Select(log => log.EmployeeId!.Value).Distinct().ToListAsync();
            var leaveIds = await _dbContext.LeaveRequests.AsNoTracking().Where(request => teamIds.Contains(request.EmployeeId) && request.Status == "Approved" && request.FromDate <= DateOnly.FromDateTime(today) && request.ToDate >= DateOnly.FromDateTime(today)).Select(request => request.EmployeeId).Distinct().ToListAsync();
            return new ManagerAttendanceViewModel { TeamMembers = team, PresentIds = presentIds.ToHashSet(), OnLeaveIds = leaveIds.ToHashSet() };
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
        public async Task<IActionResult> ManagerQueries()
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Include(ticket => ticket.Employee)
                .Where(ticket => ticket.ReportingManagerId == managerId && ticket.Status != "Resolved" && ticket.Status != "Closed")
                .OrderByDescending(ticket => ticket.CreatedAtUtc).ToListAsync();
            return View(new ManagerSectionViewModel { QueryTickets = tickets });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager")]
        public async Task<IActionResult> DecideTeamLeave(int id, string decision, string? note)
        {
            var managerId = await GetLoggedInEmployeeIdAsync();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
            var request = await _dbContext.LeaveRequests.Include(item => item.Employee).FirstOrDefaultAsync(item => item.Id == id && item.AssignedApproverEmployeeId == managerId && item.Status == "Pending");
            if (request == null) return NotFound();
            request.Status = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Rejected";
            request.DecidedByUserId = userId; request.DecidedAtUtc = DateTime.UtcNow; request.DecisionNote = CleanProfileValue(note);
            await _dbContext.SaveChangesAsync();
            TempData["WorkflowMessage"] = $"Leave request {request.Status.ToLowerInvariant()}. HR can now see the updated status.";
            return RedirectToAction(nameof(ManagerLeaves));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager,HR,Admin")]
        public async Task<IActionResult> UpdateQueryTicket(int id, string status, string? resolution)
        {
            var ticket = await _dbContext.QueryTickets.FirstOrDefaultAsync(item => item.Id == id);
            if (ticket == null) return NotFound();
            var managerId = await GetLoggedInEmployeeIdAsync();
            if (User.IsInRole("Manager") && ticket.ReportingManagerId != managerId) return Forbid();
            ticket.Status = status is "Resolved" or "Closed" ? status : "In Progress";
            ticket.Resolution = CleanProfileValue(resolution);
            ticket.UpdatedAtUtc = DateTime.UtcNow;
            ticket.ResolvedByUserId = ticket.Status is "Resolved" or "Closed"
                && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(User.IsInRole("Manager") ? nameof(ManagerQueries) : nameof(WorkflowManagement));
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> SalarySlips()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var salary = await _dbContext.EmployeeSalaryDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id && item.IsActive);
            var today = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var months = Enumerable.Range(0, 12).Select(offset => today.AddMonths(-offset))
                .Where(date => DateOnly.FromDateTime(date) >= new DateOnly(employee.JoiningDate.Year, employee.JoiningDate.Month, 1))
                .Select(date => new SalarySlipMonth(date.Year, date.Month, date.ToString("MMMM yyyy"), salary != null && date >= new DateTime(salary.EffectiveFrom.Year, salary.EffectiveFrom.Month, 1))).ToList();
            return View(new SalarySlipPageViewModel { Employee = employee, Months = months });
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> DownloadSalarySlip(int year, int month)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (month is < 1 or > 12 || year < employee.JoiningDate.Year || new DateTime(year, month, 1) > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
                return BadRequest("Invalid salary slip month.");
            var salary = await _dbContext.EmployeeSalaryDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id && item.IsActive);
            if (salary == null) return BadRequest("Salary details have not been added by HR.");
            if (new DateOnly(year, month, 1) < new DateOnly(salary.EffectiveFrom.Year, salary.EffectiveFrom.Month, 1)) return BadRequest("Salary details are not effective for this month.");
            var bank = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(item => item.EmployeeId == employee.Id);
            var pdf = SalarySlipPdfService.Create(employee, salary, bank, year, month);
            return File(pdf, "application/pdf", $"Salary-Slip-{employee.EmployeeCode}-{year}-{month:00}.pdf");
        }

        [Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> WorkflowManagement()
        {
            var leaves = await _dbContext.LeaveRequests.AsNoTracking().Include(item => item.Employee).Include(item => item.AssignedApproverEmployee)
                .Where(item => item.Status == "Pending").OrderByDescending(item => item.AppliedAtUtc).ToListAsync();
            var tickets = await _dbContext.QueryTickets.AsNoTracking().Include(item => item.Employee).Include(item => item.ReportingManager)
                .Where(item => item.Status != "Resolved" && item.Status != "Closed")
                .OrderByDescending(item => item.CreatedAtUtc).ToListAsync();
            return View(new WorkflowManagementViewModel { LeaveRequests = leaves, QueryTickets = tickets });
        }

        [Authorize(Roles = "Manager,HR,Admin")]
        public async Task<IActionResult> ClosedIssues(string? search, string? department, string? status)
        {
            var isManager = User.IsInRole("Manager");
            var managerId = isManager ? await GetLoggedInEmployeeIdAsync() : null;
            var baseQuery = _dbContext.QueryTickets.AsNoTracking()
                .Where(ticket => (ticket.Status == "Resolved" || ticket.Status == "Closed")
                    && (!isManager || ticket.ReportingManagerId == managerId));
            var departments = await baseQuery.Select(ticket => ticket.Employee.Department)
                .Distinct().OrderBy(name => name).ToListAsync();
            IQueryable<QueryTicket> query = baseQuery.Include(ticket => ticket.Employee)
                .Include(ticket => ticket.ReportingManager)
                .Include(ticket => ticket.ResolvedByUser);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(ticket => ticket.Employee.FullName.Contains(term)
                    || ticket.Employee.EmployeeCode.Contains(term)
                    || ticket.Subject.Contains(term)
                    || ticket.Description.Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(ticket => ticket.Employee.Department == department);
            if (status is "Resolved" or "Closed")
                query = query.Where(ticket => ticket.Status == status);

            var tickets = await query.OrderByDescending(ticket => ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc).ToListAsync();
            return View(new ClosedIssuesViewModel
            {
                Tickets = tickets, Departments = departments, Search = search,
                Department = department, Status = status, IsManagerView = isManager
            });
        }

        [Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> LeaveStatus(string? search, string? department, string? status)
        {
            var baseQuery = _dbContext.LeaveRequests.AsNoTracking()
                .Where(request => request.Status == "Approved" || request.Status == "Rejected");
            var departments = await baseQuery.Select(request => request.Employee.Department)
                .Distinct().OrderBy(name => name).ToListAsync();
            IQueryable<LeaveRequest> query = baseQuery
                .Include(request => request.Employee)
                .Include(request => request.AssignedApproverEmployee)
                .Include(request => request.DecidedByUser);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(request => request.Employee.FullName.Contains(term)
                    || request.Employee.EmployeeCode.Contains(term)
                    || request.LeaveType.Contains(term)
                    || request.Reason.Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(request => request.Employee.Department == department);
            if (status is "Approved" or "Rejected")
                query = query.Where(request => request.Status == status);

            var requests = await query.OrderByDescending(request => request.DecidedAtUtc ?? request.AppliedAtUtc).ToListAsync();
            return View(new LeaveStatusViewModel
            {
                Requests = requests, Departments = departments, Search = search,
                Department = department, Status = status
            });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR,Admin")]
        public async Task<IActionResult> DecideManagerLeave(int id, string decision, string? note)
        {
            var request = await _dbContext.LeaveRequests.Include(item => item.Employee).FirstOrDefaultAsync(item => item.Id == id && item.ApprovalLevel == "HR/Admin" && item.Status == "Pending");
            if (request == null) return NotFound();
            request.Status = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Rejected";
            request.DecidedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
            request.DecidedAtUtc = DateTime.UtcNow; request.DecisionNote = CleanProfileValue(note);
            await _dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(WorkflowManagement));
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
        [Authorize(Roles = "Employee,User,Manager,HR")]
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
            var employee = await _dbContext.Employees.AsNoTracking().FirstAsync(item => item.Id == employeeId.Value);
            var seniorApplicant = User.IsInRole("Manager") || User.IsInRole("HR");
            _dbContext.LeaveRequests.Add(new LeaveRequest { EmployeeId = employee.Id, LeaveType = leaveType.Trim(), FromDate = fromDate, ToDate = toDate, Reason = reason.Trim(), ApprovalLevel = seniorApplicant ? "HR/Admin" : "Manager", AssignedApproverEmployeeId = seniorApplicant ? null : employee.ReportingManagerId });
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
                ? await _dbContext.AppUsers.AsNoTracking().Where(user => user.Id == userId && user.IsActive).Select(user => user.EmployeeId).FirstOrDefaultAsync()
                : null;
        }

        private async Task<string?> SaveFieldAttendanceSelfieAsync(string? dataUrl, int employeeId, string action)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return null;
            const string prefix = "data:image/";
            if (!dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !dataUrl.Contains(";base64,"))
                throw new InvalidOperationException("Invalid selfie image.");
            var encoded = dataUrl[(dataUrl.IndexOf(";base64,", StringComparison.Ordinal) + 8)..];
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length is 0 or > 5 * 1024 * 1024)
                throw new InvalidOperationException("Selfie must be smaller than 5 MB.");

            var folder = Path.Combine(_environment.WebRootPath, "uploads", "field-attendance");
            Directory.CreateDirectory(folder);
            var fileName = $"{employeeId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{action.Replace(" ", "").ToLowerInvariant()}-{Guid.NewGuid():N}.jpg";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
            return $"/uploads/field-attendance/{fileName}";
        }

        private async Task<Employee?> LoadLoggedInEmployeeAsync()
        {
            var employeeId = await GetLoggedInEmployeeIdAsync();
            if (!employeeId.HasValue && AccountRoleService.Normalize(User.FindFirstValue(ClaimTypes.Role)) == AccountRoleService.HR)
            {
                employeeId = await EnsureHrSelfServiceProfileAsync();
            }
            return employeeId.HasValue
                ? await _dbContext.Employees.AsNoTracking().Include(employee => employee.ReportingManager).Include(employee => employee.DepartmentEntity).FirstOrDefaultAsync(employee => employee.Id == employeeId.Value && employee.IsActive)
                : null;
        }

        private async Task<int?> EnsureHrSelfServiceProfileAsync()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdText, out var userId)) return null;

            var user = await _dbContext.AppUsers.FirstOrDefaultAsync(item => item.Id == userId && item.IsActive);
            if (user == null || AccountRoleService.Normalize(user.Role) != AccountRoleService.HR) return null;
            if (user.EmployeeId.HasValue) return user.EmployeeId;

            var normalizedName = user.FullName.Trim().ToLower();
            var normalizedUsername = user.Username.Trim().ToLower();
            var existingEmployee = await _dbContext.Employees.FirstOrDefaultAsync(employee =>
                employee.IsActive &&
                (employee.FullName.ToLower() == normalizedName || employee.Email.ToLower() == normalizedUsername));

            if (existingEmployee == null)
            {
                var baseCode = $"HR-{user.Id:0000}";
                var employeeCode = baseCode;
                var suffix = 1;
                while (await _dbContext.Employees.AnyAsync(employee => employee.EmployeeCode == employeeCode))
                {
                    employeeCode = $"{baseCode}-{suffix++}";
                }

                var email = user.Username.Contains('@') ? user.Username.Trim() : $"{user.Username.Trim()}@vertex.local";
                existingEmployee = new Employee
                {
                    EmployeeCode = employeeCode,
                    FirstName = string.IsNullOrWhiteSpace(user.FullName) ? "HR" : user.FullName.Trim(),
                    FullName = string.IsNullOrWhiteSpace(user.FullName) ? "HR" : user.FullName.Trim(),
                    Email = email,
                    PhoneNumber = "Not provided",
                    EmergencyContact = "Not provided",
                    JoiningDate = DateOnly.FromDateTime(DateTime.Today),
                    Department = "Human Resources",
                    Designation = "HR Executive",
                    EmploymentType = "Full Time",
                    EmployeeStatus = "Active",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };
                _dbContext.Employees.Add(existingEmployee);
                await _dbContext.SaveChangesAsync();
            }

            user.EmployeeId = existingEmployee.Id;
            await _dbContext.SaveChangesAsync();
            return existingEmployee.Id;
        }

        [Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> MyBankDetails()
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            var detail = await _dbContext.EmployeeBankDetails.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employee.Id);
            var requests = await _dbContext.BankDetailUpdateRequests.AsNoTracking().Where(x => x.EmployeeId == employee.Id).OrderByDescending(x => x.RequestedAtUtc).ToListAsync();
            return View(new MyBankDetailsViewModel { Employee = employee, BankDetail = detail, Requests = requests, UpdateRequest = new BankDetailRequestViewModel { AccountHolderName = detail?.AccountHolderName ?? employee.FullName, BankName = detail?.BankName ?? string.Empty, IfscCode = detail?.IfscCode ?? string.Empty, BranchName = detail?.BranchName, AccountType = detail?.AccountType ?? "Savings", PanNumber = detail?.PanNumber, UanNumber = detail?.UanNumber, EsicNumber = detail?.EsicNumber, UpiId = detail?.UpiId } });
        }

        [HttpGet, Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> RequestBankUpdate()
        {
            return await LoadLoggedInEmployeeAsync() == null ? RedirectToAction(nameof(AccessDenied)) : View(new BankDetailRequestViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User,Manager,HR")]
        public async Task<IActionResult> RequestBankUpdate(BankDetailRequestViewModel model)
        {
            var employee = await LoadLoggedInEmployeeAsync();
            if (employee == null) return RedirectToAction(nameof(AccessDenied));
            if (!ModelState.IsValid) return View(model);
            if (await _dbContext.BankDetailUpdateRequests.AnyAsync(x => x.EmployeeId == employee.Id && x.Status == "Pending"))
            { ModelState.AddModelError(string.Empty, "A bank update request is already pending with HR."); return View(model); }
            var account = model.AccountNumber.Trim();
            _dbContext.BankDetailUpdateRequests.Add(new BankDetailUpdateRequest { EmployeeId = employee.Id, AccountHolderName = model.AccountHolderName.Trim(), BankName = model.BankName.Trim(), ProtectedAccountNumber = _bankProtection.Protect(account), AccountLastFour = account[^4..], IfscCode = model.IfscCode.Trim().ToUpperInvariant(), BranchName = CleanProfileValue(model.BranchName), AccountType = model.AccountType, PanNumber = CleanProfileValue(model.PanNumber)?.ToUpperInvariant(), UanNumber = CleanProfileValue(model.UanNumber), EsicNumber = CleanProfileValue(model.EsicNumber), UpiId = CleanProfileValue(model.UpiId) });
            await _dbContext.SaveChangesAsync(); TempData["BankMessage"] = "Bank detail request submitted to HR for verification."; return RedirectToAction(nameof(MyBankDetails));
        }

        [Authorize(Roles = "HR")]
        public async Task<IActionResult> BankUpdateRequests()
        {
            var requests = await _dbContext.BankDetailUpdateRequests.AsNoTracking().Include(x => x.Employee).OrderByDescending(x => x.RequestedAtUtc).ToListAsync();
            return View(new BankApprovalViewModel { Requests = requests });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR")]
        public async Task<IActionResult> ReviewBankUpdate(int id, string decision, string? note)
        {
            var request = await _dbContext.BankDetailUpdateRequests.FirstOrDefaultAsync(x => x.Id == id && x.Status == "Pending");
            if (request == null) return NotFound();
            request.Status = decision == "Approved" ? "Approved" : "Rejected"; request.HrNote = CleanProfileValue(note); request.ReviewedAtUtc = DateTime.UtcNow; request.ReviewedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
            if (request.Status == "Approved")
            {
                var detail = await _dbContext.EmployeeBankDetails.FirstOrDefaultAsync(x => x.EmployeeId == request.EmployeeId) ?? new EmployeeBankDetail { EmployeeId = request.EmployeeId };
                detail.AccountHolderName = request.AccountHolderName; detail.BankName = request.BankName; detail.ProtectedAccountNumber = request.ProtectedAccountNumber; detail.AccountLastFour = request.AccountLastFour; detail.IfscCode = request.IfscCode; detail.BranchName = request.BranchName; detail.AccountType = request.AccountType; detail.PanNumber = request.PanNumber; detail.UanNumber = request.UanNumber; detail.EsicNumber = request.EsicNumber; detail.UpiId = request.UpiId; detail.IsVerified = true; detail.VerifiedByUserId = request.ReviewedByUserId; detail.VerifiedAtUtc = DateTime.UtcNow; detail.UpdatedAtUtc = DateTime.UtcNow;
                if (detail.Id == 0) _dbContext.EmployeeBankDetails.Add(detail);
            }
            await _dbContext.SaveChangesAsync(); return RedirectToAction(nameof(BankUpdateRequests));
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





