using System.ComponentModel.DataAnnotations;
namespace VertexERP.Models;
public sealed class ModuleState
{
    [Key, MaxLength(50)] public string Id { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [ConcurrencyCheck] public long Revision { get; set; }
}
