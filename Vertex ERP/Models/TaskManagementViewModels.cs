using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class CreateWorkTaskRequest
{
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    [Required] public int? ManagerId { get; set; }
    [Required] public int? AssigneeId { get; set; }
    [Required] public string Priority { get; set; } = "Medium";
    [Required] public DateOnly? DueDate { get; set; }
}
