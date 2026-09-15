using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
namespace VertexERP.Services;
public sealed class ModuleAccessService(ApplicationDbContext db)
{
    private Dictionary<string, bool>? states;
    public async Task<Dictionary<string, bool>> StatesAsync() => states ??= await db.ModuleStates.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.IsActive);
    public async Task<bool> AllowedAsync(string? controller, string? action)
    {
        var id = ModuleCatalog.ForRoute(controller, action);
        return id == null || !(await StatesAsync()).TryGetValue(id, out var active) || active;
    }
}
