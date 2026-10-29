using Microsoft.AspNetCore.Routing;
namespace VertexERP.Services;

public static class ErpRouteConfiguration
{
    public static void MapErpRoutes(this IEndpointRouteBuilder endpoints)
    {
        // The global default action is Start; inbox controllers use Index instead.
        endpoints.MapControllerRoute(name: "hr-leave", pattern: "HrLeave/{action=Index}/{id?}", defaults: new { controller = "HrLeave" });
        endpoints.MapControllerRoute(name: "notifications", pattern: "Notifications/{action=Index}/{id?}", defaults: new { controller = "Notifications" });
        endpoints.MapControllerRoute(name: "expense", pattern: "Expense/{action=Index}/{id?}", defaults: new { controller = "Expense" });
        endpoints.MapControllerRoute(name: "ai-assistant", pattern: "AiAssistant", defaults: new { controller = "AiAssistant", action = "Index" });
        endpoints.MapControllerRoute(name: "default", pattern: "{controller=Main}/{action=Start}/{id?}");
    }
}
