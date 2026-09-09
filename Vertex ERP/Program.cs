using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;
using PdfSharp.Fonts;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// DOCUMENT TEMPLATES / PDF FONTS
// ============================================================

var documentTemplateDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "DocumentTemplates"
);

GlobalFontSettings.FontResolver =
    new EmployeeDocumentFontResolver(documentTemplateDirectory);

// ============================================================
// LOGGING
// ============================================================

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ============================================================
// DATABASE CONNECTION
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found."
    );

// ============================================================
// MVC
// ============================================================

builder.Services.AddControllersWithViews();

// ============================================================
// ANTIFORGERY
// ============================================================

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// ============================================================
// ATTENDANCE
// ============================================================

builder.Services.Configure<AttendanceOptions>(
    builder.Configuration.GetSection(
        AttendanceOptions.SectionName
    )
);

builder.Services.AddScoped<IBiometricRepository, BiometricRepository>();
builder.Services.AddScoped<IBiometricDeviceService, BiometricDeviceService>();
builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();

// EasyTime Pro remote API synchronization runs inside the ERP process. This
// is compatible with Azure App Service, where a second executable/port cannot
// be relied upon to stay running beside the web application.
builder.Services.Configure<RemoteAttendanceOptions>(
    builder.Configuration.GetSection(RemoteAttendanceOptions.SectionName)
);

builder.Services
    .AddHttpClient(RemoteAttendanceImportService.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VertexERP-AttendanceSync/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseCookies = true,
        CookieContainer = new System.Net.CookieContainer(),
        AllowAutoRedirect = true
    });

builder.Services.AddHostedService<RemoteAttendanceImportService>();

// ============================================================
// DTDC / SHIPMENT TRACKING
// ============================================================

builder.Services.AddHttpClient<
    IShipmentTrackingService,
    DtdcTrackingService
>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);

    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "VertexERP/1.0"
    );
});

// ============================================================
// DATA PROTECTION
// ============================================================

var keyDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data",
    "DataProtectionKeys"
);

Directory.CreateDirectory(keyDirectory);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(keyDirectory)
    )
    .SetApplicationName("VertexERP");

builder.Services.AddScoped<BankAccountProtectionService>();
builder.Services.AddScoped<IPasswordResetEmailService, PasswordResetEmailService>();

// ============================================================
// SESSION
// ============================================================

builder.Services.AddSession();

// ============================================================
// AUTHENTICATION
// ============================================================

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme
    )
    .AddCookie(options =>
    {
        options.LoginPath = "/Main/Login";
        options.AccessDeniedPath = "/Main/AccessDenied";

        options.Cookie.Name = "VertexERP.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Keep the authenticated profile synchronized with the database. If an
        // administrator changes a user's role, name, username, or active state,
        // the old cookie must not continue granting the previous profile.
        options.Events.OnValidatePrincipal = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
            {
                context.RejectPrincipal();
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var currentUser = await db.AppUsers.AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == userId && user.IsActive);
            var currentRole = AccountRoleService.Normalize(currentUser?.Role);
            if (currentUser == null || currentRole == null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var cookieRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var cookieName = context.Principal?.FindFirstValue(ClaimTypes.Name);
            var cookieUsername = context.Principal?.FindFirstValue("username");
            if (cookieRole == currentRole && cookieName == currentUser.FullName && cookieUsername == currentUser.Username)
                return;

            var refreshedClaims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, currentUser.Id.ToString()),
                new(ClaimTypes.Name, currentUser.FullName),
                new(ClaimTypes.Role, currentRole),
                new("username", currentUser.Username)
            };
            context.ReplacePrincipal(new ClaimsPrincipal(
                new ClaimsIdentity(refreshedClaims, CookieAuthenticationDefaults.AuthenticationScheme)));
            context.ShouldRenew = true;
            context.HttpContext.Session.SetString("email", currentUser.Username);
            context.HttpContext.Session.SetString("username", currentUser.Username);
            context.HttpContext.Session.SetString("role", currentRole);
            context.HttpContext.Session.SetString("fullName", currentUser.FullName);
        };
    });

builder.Services.AddAuthorization();

// ============================================================
// POSTGRESQL / ENTITY FRAMEWORK CORE
// ============================================================

builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure();
            }
        )
);

// ============================================================
// BUILD APPLICATION
// ============================================================

var app = builder.Build();

// ============================================================
// DATABASE MIGRATION & SEEDING
// ============================================================

using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    dbContext.Database.Migrate();

    var defaultPassword =
        builder.Configuration["SeedUsers:Password"] ?? "password";

    DatabaseInitializer.SeedDevelopmentUsers(
        dbContext,
        defaultPassword
    );
}

// ============================================================
// HTTP REQUEST PIPELINE
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

// ============================================================
// HTTPS
// ============================================================

var configuredUrls =
    builder.Configuration["ASPNETCORE_URLS"]
    ?? string.Empty;

if (
    configuredUrls.Contains(
        "https://",
        StringComparison.OrdinalIgnoreCase
    )
)
{
    app.UseHttpsRedirection();
}

// ============================================================
// STATIC FILES
// ============================================================

app.UseStaticFiles();

// ============================================================
// ROUTING
// ============================================================

app.UseRouting();

// ============================================================
// SESSION
// ============================================================

app.UseSession();

// ============================================================
// AUTHENTICATION
// ============================================================

app.UseAuthentication();

// ============================================================
// AUTHORIZATION
// ============================================================

app.UseAuthorization();

// ============================================================
// MVC ROUTING
// ============================================================

app.MapControllerRoute(
    name: "expense",
    pattern: "Expense/{action=Index}/{id?}",
    defaults: new { controller = "Expense" }
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Main}/{action=Start}/{id?}"
);

// ============================================================
// START APPLICATION
// ============================================================

var appUrl = "http://localhost:5000";
var isAzureAppService =
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

if (!isAzureAppService)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = appUrl + "/Main/Start",
                    UseShellExecute = true
                }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Could not open browser automatically: {ex.Message}"
            );
        }
    });
}

// The biometric devices post punches to the lightweight receiver on port 8082.
// Keep it available whenever the local ERP application is running; otherwise the
// attendance screen correctly has no punches to show for the current day.
if (!isAzureAppService)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var receiverIsRunning = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == 8082);
            if (receiverIsRunning) return;

            var receiverPath = new[]
            {
                Path.Combine(builder.Environment.ContentRootPath, "BiometricReceiver", "BiometricReceiver.exe"),
                Path.Combine(builder.Environment.ContentRootPath, ".codex-biometric-runtime", "BiometricReceiver.exe")
            }.FirstOrDefault(File.Exists);
            if (receiverPath == null) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = receiverPath,
                WorkingDirectory = Path.GetDirectoryName(receiverPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not start biometric receiver: {ex.Message}");
        }
    });
}

app.Run();
