using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class WorkTask
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int ManagerId { get; set; }
    public Employee Manager { get; set; } = null!;

    public int AssigneeId { get; set; }
    public Employee Assignee { get; set; } = null!;

    [Required, MaxLength(20)]
    public string Priority { get; set; } = "Medium";

    [Required, MaxLength(30)]
    public string Status { get; set; } = "To Do";

    public DateOnly DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
