using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

// Add services to the container.
builder.Services.AddControllersWithViews();
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
    options.UseSqlite(authConnectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    DatabaseInitializer.SeedDefaultUsers(dbContext);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// Every ERP page requires an authenticated session.  The login endpoint stays
// anonymous so a user can always reach it, including after an expired session.
app.Use(async (context, next) =>
{
    var controller = context.Request.RouteValues["controller"]?.ToString();
    var action = context.Request.RouteValues["action"]?.ToString();
    var isLoginRequest = string.Equals(controller, "Guest", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action, "Login", StringComparison.OrdinalIgnoreCase);

    if (!isLoginRequest && string.IsNullOrWhiteSpace(context.Session.GetString("username")))
    {
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect($"/Guest/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
   pattern: "{controller=Guest}/{action=Login}/{id?}");

app.Run();
