namespace VertexERP.Models;

using System.ComponentModel.DataAnnotations;

public sealed class EmployeePortalViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
    public IReadOnlyList<LeaveRequest> LeaveRequests { get; init; } = Array.Empty<LeaveRequest>();
    public DateTime? CheckIn { get; init; }
    public DateTime? CheckOut { get; init; }
    public int Completed => Tasks.Count(task => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int InProgress => Tasks.Count(task => task.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase));
    public int Pending => Tasks.Count - Completed;
    public int Overdue => Tasks.Count(task => !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && task.DueDate < DateOnly.FromDateTime(DateTime.Today));
}

public sealed class WorkforceOverviewViewModel
{
    public IReadOnlyList<Employee> Employees { get; init; } = Array.Empty<Employee>();
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
    public IReadOnlySet<int> PresentEmployeeIds { get; init; } = new HashSet<int>();
    public int Present => PresentEmployeeIds.Count;
    public int Absent => Math.Max(0, Employees.Count(employee => employee.IsActive) - Present);
}

public sealed class EmployeeTasksViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<WorkTask> Tasks { get; init; } = Array.Empty<WorkTask>();
    public int Upcoming30Days => Tasks.Count(task => task.DueDate >= DateOnly.FromDateTime(DateTime.Today) && task.DueDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)) && !task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int Completed => Tasks.Count(task => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int InProgress => Tasks.Count(task => task.Status.Equals("In Progress", StringComparison.OrdinalIgnoreCase));
    public int Pending => Tasks.Count - Completed - InProgress;
}

public sealed record EmployeeAttendanceDay(DateOnly Date, DateTime? CheckIn, DateTime? CheckOut, string Status);

public sealed class EmployeeAttendanceViewModel
{
    public Employee Employee { get; init; } = null!;
    public DateOnly Month { get; init; }
    public IReadOnlyList<EmployeeAttendanceDay> Days { get; init; } = Array.Empty<EmployeeAttendanceDay>();
    public int Present => Days.Count(day => day.Status == "Present");
    public int Absent => Days.Count(day => day.Status == "Absent");
    public int OnLeave => Days.Count(day => day.Status == "On Leave");
}

public sealed class EmployeeLeaveViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<LeaveRequest> Requests { get; init; } = Array.Empty<LeaveRequest>();
    public int CasualUsed => UsedDays("Casual Leave");
    public int SickUsed => UsedDays("Sick Leave");
    public int EarnedUsed => UsedDays("Earned Leave");
    public int CasualRemaining => Math.Max(0, 12 - CasualUsed);
    public int SickRemaining => Math.Max(0, 8 - SickUsed);
    public int EarnedRemaining => Math.Max(0, 12 - EarnedUsed);
    private int UsedDays(string type) => Requests.Where(request => request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) && request.LeaveType.Equals(type, StringComparison.OrdinalIgnoreCase)).Sum(request => request.ToDate.DayNumber - request.FromDate.DayNumber + 1);
}

public sealed record EmployeeNotificationItem(string Title, string Detail, DateTime CreatedAt, string Type);

public sealed class EmployeeNotificationsViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<EmployeeNotificationItem> Items { get; init; } = Array.Empty<EmployeeNotificationItem>();
}

public sealed class EmployeeQueryViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<QueryTicket> Tickets { get; init; } = Array.Empty<QueryTicket>();
}

public sealed class WorkflowManagementViewModel
{
    public IReadOnlyList<LeaveRequest> LeaveRequests { get; init; } = Array.Empty<LeaveRequest>();
    public IReadOnlyList<QueryTicket> QueryTickets { get; init; } = Array.Empty<QueryTicket>();
}

public sealed class EmployeeProfileEditViewModel
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    [DataType(DataType.Date)] public DateOnly? DateOfBirth { get; set; }
    [StringLength(20)] public string? Gender { get; set; }
    [StringLength(20)] public string? MaritalStatus { get; set; }
    [Required, RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Emergency Contact must contain exactly 10 digits.")] public string EmergencyContact { get; set; } = string.Empty;
    [Required] public int? DepartmentId { get; set; }
    [Required, StringLength(80)] public string Designation { get; set; } = string.Empty;
    public int? ReportingManagerId { get; set; }
    [Required, DataType(DataType.Date)] public DateOnly JoiningDate { get; set; }
    [Required, StringLength(30)] public string EmploymentType { get; set; } = string.Empty;
    [StringLength(120)] public string? WorkLocation { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(80)] public string? State { get; set; }
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "PIN code must contain exactly 6 digits.")] public string? PinCode { get; set; }
    public IReadOnlyList<Department> Departments { get; set; } = Array.Empty<Department>();
    public IReadOnlyList<Employee> Managers { get; set; } = Array.Empty<Employee>();
}
