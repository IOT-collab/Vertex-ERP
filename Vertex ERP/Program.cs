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

app.Run();
