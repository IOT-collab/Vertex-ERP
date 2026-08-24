using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;
using PdfSharp.Fonts;

var builder = WebApplication.CreateBuilder(args);

var documentTemplateDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "DocumentTemplates"
);

GlobalFontSettings.FontResolver =
    new EmployeeDocumentFontResolver(documentTemplateDirectory);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// The browser application owns only its local UI port.
// The standalone BiometricReceiver process owns LAN port 8082,
// so attendance stays available even when Visual Studio restarts this application.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7090);
});

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found."
    );

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.Configure<AttendanceOptions>(
    builder.Configuration.GetSection(AttendanceOptions.SectionName)
);

builder.Services.AddScoped<IBiometricRepository, BiometricRepository>();
builder.Services.AddScoped<IBiometricDeviceService, BiometricDeviceService>();
builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();

builder.Services.AddHttpClient<IShipmentTrackingService, DtdcTrackingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VertexERP/1.0");
});

// Data Protection Keys
var keyDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data",
    "DataProtectionKeys"
);

Directory.CreateDirectory(keyDirectory);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("VertexERP");

builder.Services.AddScoped<BankAccountProtectionService>();

// Session
builder.Services.AddSession();

// Authentication
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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
    });

builder.Services.AddAuthorization();

// PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure();
        }
    )
);

var app = builder.Build();

// ============================================================
// DATABASE MIGRATION
// ============================================================

using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    dbContext.Database.Migrate();

    var developmentUserPassword =
        builder.Configuration["SeedUsers:Password"];

    if (
        app.Environment.IsDevelopment()
        && !string.IsNullOrWhiteSpace(developmentUserPassword)
    )
    {
        DatabaseInitializer.SeedDevelopmentUsers(
            dbContext,
            developmentUserPassword
        );
    }
}

// ============================================================
// HTTP REQUEST PIPELINE
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    // The default HSTS value is 30 days.
    app.UseHsts();
}

// Local biometric deployments use a direct Ethernet HTTP endpoint.
// Redirect only when an HTTPS address is actually configured.
var configuredUrls =
    builder.Configuration["ASPNETCORE_URLS"] ?? string.Empty;

if (
    configuredUrls.Contains(
        "https://",
        StringComparison.OrdinalIgnoreCase
    )
)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

// ============================================================
// MVC ROUTING
// ============================================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Main}/{action=Start}/{id?}"
);

// ============================================================
// AUTOMATICALLY OPEN ERP IN BROWSER
// ============================================================

app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        // Small delay gives Kestrel enough time to start listening.
        Task.Run(async () =>
        {
            await Task.Delay(1000);

            var url = "http://localhost:7090";

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                }
            );
        });
    }
    catch
    {
        // If browser cannot be opened, the ERP server
        // will continue running normally.
    }
});

// ============================================================
// START APPLICATION
// ============================================================

app.Run();