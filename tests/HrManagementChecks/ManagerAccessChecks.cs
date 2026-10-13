using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Controllers;
using VertexERP.Services;

static class ManagerAccessChecks
{
    public static async Task Run()
    {
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        Check(AccountRoleService.EffectiveRole("Employee", true) == "Manager", "Assigned reporting manager receives manager access");
        Check(AccountRoleService.EffectiveRole("User", true) == "Manager", "Legacy employee account with assigned team receives manager access");
        Check(AccountRoleService.EffectiveRole("Employee", false) == "Employee", "Employee without assigned team remains employee");
        Check(AccountRoleService.EffectiveRole("Manager", false) == "Manager", "Explicit manager without team retains manager dashboard");
        Check(AccountRoleService.EffectiveRole("Supervisor", false) == "Manager", "Supervisor alias retains manager access");
        Check(AccountRoleService.EffectiveRole("HR", true) == "HR" && AccountRoleService.EffectiveRole("Admin", true) == "Admin", "HR and Admin retain their access");
        Check(AccountRoleService.EffectiveRole("invalid", true) == null, "Invalid role cannot gain access through assignments");
        var controller = new MainController(null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Manager") }, "test"))
            } }
        };
        Check(await controller.Employees(null) is RedirectToActionResult { ActionName: "Manager" }, "Manager visiting old employee dashboard goes to Manager dashboard");
        Check(typeof(MainController).GetMethod("MyTeam")!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "Manager", "My Team requires manager authorization");
        Check(ModuleCatalog.ForRoute("Main", "MyTeam") == "hr", "My Team respects HR module access settings");
        Console.WriteLine($"{passed} manager access checks passed.");
    }
}
