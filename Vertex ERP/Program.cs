using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<AttendanceOptions>(builder.Configuration.GetSection(AttendanceOptions.SectionName));
builder.Services.AddScoped<IBiometricRepository, BiometricRepository>();
builder.Services.AddScoped<IBiometricDeviceService, BiometricDeviceService>();
builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();
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

// K40 Pro ADMS firmware posts to HTTP when HTTPS is disabled on the device.
// Keep HTTPS redirection for the ERP UI and exempt only the isolated ADMS receiver.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/iclock"),
    branch => branch.UseHttpsRedirection());
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
   pattern: "{controller=Main}/{action=Start}/{id?}");

app.Run();
