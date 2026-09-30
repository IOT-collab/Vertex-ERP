using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VertexERP.Models;

public sealed class ProjectTeam
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class ProjectTeamMember
{
    public int TeamId { get; set; }
    public ProjectTeam Team { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
}
public sealed class ProjectTimeEntry
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [ValidateNever] public ErpProject Project { get; set; } = null!;
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [ValidateNever] public Employee Employee { get; set; } = null!;
    public DateOnly WorkDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Range(0.01, 24)] public decimal Hours { get; set; }
    [Required, MaxLength(1000)] public string Notes { get; set; } = "";
}
public sealed class ProjectCost
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [ValidateNever] public ErpProject Project { get; set; } = null!;
    [Required, MaxLength(150)] public string Title { get; set; } = "";
    [Range(0.01, 1000000000)] public decimal Amount { get; set; }
    public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [MaxLength(1000)] public string? Notes { get; set; }
}
public sealed class ProjectEnquiry
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [ValidateNever] public ErpProject Project { get; set; } = null!;
    [Required, MaxLength(150)] public string Subject { get; set; } = "";
    [Required, MaxLength(2000)] public string Description { get; set; } = "";
    [Required, RegularExpression("^(Open|In Progress|Resolved|Closed)$")] public string Status { get; set; } = "Open";
    [MaxLength(2000)] public string? Resolution { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class ProjectTaskInput
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [Range(1, int.MaxValue)] public int AssigneeId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    [Required, RegularExpression("^(Low|Medium|High)$")] public string Priority { get; set; } = "Medium";
    [Required, RegularExpression("^(To Do|In Progress|In Review|Completed)$")] public string Status { get; set; } = "To Do";
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
public sealed class ProjectWorkspace
{
    public string Section { get; set; } = "projects";
    public bool CanManageProjects { get; set; }
    public int? SelectedProjectId { get; set; }
    public List<ErpProject> Projects { get; set; } = [];
    public List<Department> Departments { get; set; } = [];
    public List<Employee> Managers { get; set; } = [];
    public List<Employee> Employees { get; set; } = [];
    public List<ProjectTeam> Teams { get; set; } = [];
    public List<ProjectTeamMember> TeamMembers { get; set; } = [];
    public List<ProjectEmployee> Assignments { get; set; } = [];
    public List<WorkTask> Tasks { get; set; } = [];
    public List<ProjectTimeEntry> Timesheets { get; set; } = [];
    public List<ProjectCost> Costs { get; set; } = [];
    public List<ProjectEnquiry> Enquiries { get; set; } = [];
    public ErpProject ProjectForm { get; set; } = new();
    public ProjectTeam TeamForm { get; set; } = new();
    public int[] MemberIds { get; set; } = [];
    public ProjectTaskInput TaskForm { get; set; } = new();
    public ProjectTimeEntry TimeForm { get; set; } = new();
    public ProjectCost CostForm { get; set; } = new();
    public ProjectEnquiry EnquiryForm { get; set; } = new();
    public IEnumerable<ErpProject> VisibleProjects => Projects.Where(p => !SelectedProjectId.HasValue || p.Id == SelectedProjectId);
}
