using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VertexERP.Data;
using VertexERP.Services;
using Vertex_ERP.Controllers;

internal static class NotificationRouteChecks
{
    public static async Task Run()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(NotificationsController).Assembly.GetName().Name,ContentRootPath=Path.GetFullPath("Vertex ERP")});
        builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(NotificationsController).Assembly);
        builder.Services.AddDbContext<ApplicationDbContext>(options=>options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1"));
        builder.Services.AddScoped<NotificationInboxService>();
        builder.Services.AddAuthentication("RouteCheck").AddScheme<AuthenticationSchemeOptions,RouteCheckAuthentication>("RouteCheck",_=>{});
        builder.Services.AddAuthorization();
        await using var app=builder.Build();
        app.UseAuthentication();app.UseAuthorization();app.MapErpRoutes();
        await app.StartAsync();
        try
        {
            var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(address)};
            foreach(var url in new[]{"/Notifications","/Notifications/Index","/Notifications/Count","/HrLeave","/HrLeave/Index"})
            {
                using var response=await client.GetAsync(url);
                if(response.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception(url+" must resolve to authentication challenge, got "+response.StatusCode);
            }
            foreach(var role in new[]{"HR","Admin","Manager","Employee","User"})
            {
                client.DefaultRequestHeaders.Remove("X-Route-Role");client.DefaultRequestHeaders.Add("X-Route-Role",role);
                foreach(var url in new[]{"/Notifications","/Notifications/Index","/Notifications/Count","/HrLeave","/HrLeave/Index"})
                {
                    // Deliberately omit account identity: action must run and reject it without touching a database.
                    using var response=await client.GetAsync(url);
                    if(response.StatusCode!=HttpStatusCode.Forbidden)throw new Exception(role+" "+url+" did not reach the account guard: "+response.StatusCode);
                }
            }
            Console.WriteLine("PASS real HTTP routes /Notifications, /Notifications/Index and /Notifications/Count for all five roles (no 404, account guard reached)");
        }
        finally { await app.StopAsync(); }
    }
    private sealed class RouteCheckAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role=Request.Headers["X-Route-Role"].ToString();
            if(string.IsNullOrEmpty(role))return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,role),new Claim(ClaimTypes.Name,"Route check")},Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
}
