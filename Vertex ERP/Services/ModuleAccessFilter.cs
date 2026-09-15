using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace VertexERP.Services;
public sealed class ModuleAccessFilter(ModuleAccessService access) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        var allowed = await access.AllowedAsync(controller, action);
        if (allowed && string.Equals(controller, "Main", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(action, "Reports", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "DownloadAdminReport", StringComparison.OrdinalIgnoreCase)))
        {
            var report = context.HttpContext.Request.Query["reportType"].ToString().ToLowerInvariant();
            var modules = report switch
            {
                "manager-report" => new[] { "hr", "tasks", "projects", "leave" },
                "project-details" => new[] { "projects" },
                "employee-tasks" or "manager-projects" => new[] { "tasks" },
                "leave-report" => new[] { "leave" },
                "punch-report" => new[] { "attendance" },
                _ when report.Contains("attendance") => new[] { "attendance" },
                "" => Array.Empty<string>(),
                _ => new[] { "hr" }
            };
            var states = await access.StatesAsync();
            allowed = modules.All(id => !states.TryGetValue(id, out var active) || active);
        }
        if (!allowed)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Module deactivated", Detail = "An administrator has deactivated this module. Your saved data is preserved." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
