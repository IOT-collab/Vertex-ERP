using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public sealed class ManualAttendanceAccess(ApplicationDbContext db)
{
    public const string Policy = "ManualAttendanceOperators";
    private bool? allowed;
    public static bool Allows(AppUser? account, Employee? employee) =>
        account is { IsActive: true } && employee is { IsActive: true, CanManageManualAttendance: true }
        && account.EmployeeId == employee.Id && AccountRoleService.Normalize(account.Role) != AccountRoleService.HR;

    public async Task<bool> CanMarkAsync(ClaimsPrincipal principal)
    {
        if (allowed.HasValue) return allowed.Value;
        if (principal.Identity?.IsAuthenticated != true || principal.IsInRole("HR")
            || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var account = await db.AppUsers.AsNoTracking().Include(user => user.Employee).SingleOrDefaultAsync(user => user.Id == id);
        allowed = Allows(account, account?.Employee);
        return allowed.Value;
    }
}

public sealed class ManualAttendanceOperatorRequirement : IAuthorizationRequirement { }
public sealed class ManualAttendanceOperatorHandler(ManualAttendanceAccess access)
    : AuthorizationHandler<ManualAttendanceOperatorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ManualAttendanceOperatorRequirement requirement)
    {
        if (await access.CanMarkAsync(context.User)) context.Succeed(requirement);
    }
}
