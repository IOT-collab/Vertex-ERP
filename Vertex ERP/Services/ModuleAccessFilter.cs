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
        if (!allowed)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Module deactivated", Detail = "An administrator has deactivated this module. Your saved data is preserved." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
