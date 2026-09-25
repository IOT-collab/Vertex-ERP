namespace VertexERP.Models;

public sealed class ProjectEmployee
{
    public int ProjectId { get; set; }
    public ErpProject Project { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
}

public sealed class TeamProjectOverview
{
    public List<Employee> Employees { get; set; } = [];
    public List<Employee> Managers { get; set; } = [];
    public List<ErpProject> Projects { get; set; } = [];
    public List<ProjectEmployee> Assignments { get; set; } = [];
    public int? ManagerId { get; set; }
    public int? ProjectId { get; set; }
    public string? Department { get; set; }
    public string Tab { get; set; } = "managers";
}
