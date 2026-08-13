using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// The browser application owns only its local UI port. The standalone
// BiometricReceiver process owns LAN port 8082, so attendance stays available
// even when Visual Studio restarts this application.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7090);
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.Configure<AttendanceOptions>(builder.Configuration.GetSection(AttendanceOptions.SectionName));
builder.Services.AddScoped<IBiometricRepository, BiometricRepository>();
builder.Services.AddScoped<IBiometricDeviceService, BiometricDeviceService>();
builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("VertexERP");
builder.Services.AddScoped<BankAccountProtectionService>();
builder.Services.AddSession();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Main/Login";
        options.AccessDeniedPath = "/Main/AccessDenied";
        options.Cookie.Name = "VertexERP.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.EnableRetryOnFailure()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();

    var developmentUserPassword = builder.Configuration["SeedUsers:Password"];
    if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentUserPassword))
    {
        DatabaseInitializer.SeedDevelopmentUsers(dbContext, developmentUserPassword);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Local biometric deployments use a direct Ethernet HTTP endpoint. Redirect only
// when an HTTPS address is actually configured for the running environment.
var configuredUrls = builder.Configuration["ASPNETCORE_URLS"] ?? string.Empty;
if (configuredUrls.Contains("https://", StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
   pattern: "{controller=Main}/{action=Start}/{id?}");

app.Run();
