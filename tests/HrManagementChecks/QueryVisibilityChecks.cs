using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Controllers;

static class QueryVisibilityChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        foreach (var name in new[] { "EmployeeQueries", "RaiseQuery", "EmployeeNotifications", "DeleteNotification" })
        {
            var roles = typeof(MainController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles!.Split(',');
            Check(new[] { "Employee", "User", "Manager", "HR" }.All(roles.Contains), name + " retains employee access after manager promotion");
        }
        var controller = new MainController(null!, null!, null!, null!, null!, null!) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Manager") }, "test"))
            } }
        };
        Check(await controller.ManagerQueries() is ForbidResult, "Unlinked manager cannot see unassigned queries");
        Check(await controller.ClosedIssues(null, null, null) is ForbidResult, "Unlinked manager cannot see unassigned history");
        Check(await controller.UpdateQueryTicket(1, "Resolved", "Done") is ForbidResult, "Unlinked manager cannot respond to unassigned query");
        Check(await controller.UpdateQueryTicket(1, "Invalid", "Done") is BadRequestResult or BadRequestObjectResult, "Invalid query status rejected");
        Check(await controller.UpdateQueryTicket(1, "Resolved", new string('x', 1501)) is BadRequestResult or BadRequestObjectResult, "Oversized response rejected");
        Console.WriteLine($"{passed} query visibility checks passed.");
    }
}
