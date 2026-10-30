using System.Security.Claims;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;
using PdfSharp.Fonts;

var builder = WebApplication.CreateBuilder(args);
// Local SMS credentials are ignored by Git; hosted deployments use environment settings.
builder.Configuration.AddJsonFile("App_Data/sms-secrets.json", optional: true, reloadOnChange: false);
builder.Configuration.AddJsonFile("App_Data/remoteattendance.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// In production, set AZURE_KEY_VAULT_NAME to the vault's DNS name (not a URI).
// The MSG91 key is fetched via the VM's managed identity. If Key Vault is
// configured but unavailable, clear any lower-priority AuthKey value first so
// the process fails closed and cannot fall back to an environment/appsettings key.
var keyVaultName = builder.Configuration["AZURE_KEY_VAULT_NAME"]?.Trim();
var keyVaultConfigured = !string.IsNullOrWhiteSpace(keyVaultName);
var keyVaultSecretLoaded = false;
var keyVaultStatus = keyVaultConfigured ? "not_loaded" : "vault_name_missing";
if (!keyVaultConfigured && !builder.Environment.IsDevelopment())
{
    // Production/staging require Key Vault; do not accept a lower-priority
    // MSG91 AuthKey environment value as a fallback if the vault is omitted.
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [Msg91AuthKeySecretMapping.ConfigurationKey] = ""
    });
}
if (keyVaultConfigured)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [Msg91AuthKeySecretMapping.ConfigurationKey] = ""
    });

    if (!System.Text.RegularExpressions.Regex.IsMatch(keyVaultName!, "^[a-zA-Z0-9-]{3,24}$"))
    {
        keyVaultStatus = "vault_name_invalid";
    }
    else
    {
        try
        {
            var vaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
            var secretClient = new SecretClient(vaultUri, new DefaultAzureCredential());
            var secretResponse = await secretClient.GetSecretAsync(Msg91AuthKeySecretMapping.SecretName);
            var authKey = secretResponse.Value.Value;
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Msg91AuthKeySecretMapping.ConfigurationKey] = authKey
            });

            // Evaluate only presence; never emit or otherwise expose the credential.
            keyVaultSecretLoaded = !string.IsNullOrWhiteSpace(authKey);
            keyVaultStatus = keyVaultSecretLoaded ? "secret_loaded" : "secret_missing_or_empty";
        }
        catch (AuthenticationFailedException)
        {
            keyVaultStatus = "authentication_failed";
        }
        catch (RequestFailedException exception)
        {
            keyVaultStatus = exception.Status switch
            {
                401 => "authentication_failed",
                403 => "authorization_failed",
                404 => "vault_or_secret_not_found",
                429 => "throttled",
                >= 500 => "key_vault_unavailable",
                _ => "key_vault_request_failed"
            };
        }
        catch (Exception)
        {
            // Do not log exception messages: SDK exceptions may contain request
            // details. Sending remains gated because AuthKey is still empty.
            keyVaultStatus = "key_vault_load_failed";
        }
    }
}

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

builder.Services.AddScoped<ModuleAccessService>();
builder.Services.AddControllersWithViews(options => options.Filters.Add<ModuleAccessFilter>());
builder.Services.AddHttpContextAccessor();

// Dispatch is off until credentials, approved template verification and sending approval are configured.
builder.Services.Configure<Msg91WhatsAppOptions>(builder.Configuration.GetSection(Msg91WhatsAppOptions.Section));
builder.Services.AddHttpClient<IMsg91WhatsAppService, Msg91WhatsAppService>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RedactLoggedHeaders(new[] { "authkey" });
builder.Services.AddScoped<IWhatsAppTaskQueue, WhatsAppTaskQueue>();
builder.Services.AddScoped<Msg91DeliveryInbox>();
builder.Services.AddScoped<IMsg91DeliveryInbox>(sp => sp.GetRequiredService<Msg91DeliveryInbox>());
builder.Services.AddScoped<WhatsAppTaskDispatcher>();
builder.Services.AddHostedService<WhatsAppTaskWorker>();


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

builder.Services.AddHostedService<BiometricEmployeeReconciliationService>();
builder.Services.AddSingleton<RemoteAttendanceImportService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RemoteAttendanceImportService>());

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
builder.Services.AddSingleton<EmployeePhotoStorageService>();
builder.Services.AddScoped<IPasswordResetEmailService, PasswordResetEmailService>();
builder.Services.AddHttpClient<IFirebaseIdTokenVerifier, FirebaseIdTokenVerifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VertexERP-FirebaseTokenVerification/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IPasswordResetSmsService, PasswordResetSmsService>(client =>
    client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// ============================================================
// SESSION
// ============================================================

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

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
            if (currentUser?.EmployeeId is int linkedEmployeeId)
            {
                var linkedEmployee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == linkedEmployeeId);
                if (linkedEmployee != null) currentUser.FullName = linkedEmployee.FullName;
            }
            var currentRole = currentUser == null ? null : await AccountRoleService.ResolveAsync(db, currentUser);
            if (currentUser == null || currentRole == null ||
                (currentUser.PasswordChangedAtUtc.HasValue && context.Properties.IssuedUtc.HasValue &&
                 context.Properties.IssuedUtc.Value < currentUser.PasswordChangedAtUtc.Value))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var cookieRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var cookieName = context.Principal?.FindFirstValue(ClaimTypes.Name);
            var cookieUsername = context.Principal?.FindFirstValue("username");
            context.HttpContext.Session.SetString("fullName", currentUser.FullName);
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

builder.Services.AddScoped<NotificationInboxService>();
builder.Services.AddScoped<ManualAttendanceAccess>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ManualAttendanceOperatorHandler>();
builder.Services.AddAuthorization(options => options.AddPolicy(ManualAttendanceAccess.Policy,
    policy => policy.RequireAuthenticatedUser().AddRequirements(new ManualAttendanceOperatorRequirement())));

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

var msg91StartupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Msg91Startup");
msg91StartupLogger.LogInformation(
    "MSG91 Key Vault configured={KeyVaultConfigured}; secretLoaded={SecretLoaded}; status={KeyVaultStatus}.",
    keyVaultConfigured, keyVaultSecretLoaded, keyVaultStatus);
var msg91StartupOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Msg91WhatsAppOptions>>().Value;
msg91StartupLogger.LogInformation(
    "MSG91 sendingReady={SendingReady}; readinessIssues={ReadinessIssues}.",
    msg91StartupOptions.Ready, string.Join(',', msg91StartupOptions.ReadinessIssues));
msg91StartupLogger.LogInformation(
    "MSG91 passwordResetOtpReady={OtpReady}; readinessIssues={ReadinessIssues}.",
    msg91StartupOptions.PasswordResetOtpReady, string.Join(',', msg91StartupOptions.PasswordResetOtpReadinessIssues));

// ============================================================
// DATABASE MIGRATION & SEEDING
// ============================================================

if (DatabaseInitializer.ShouldInitializeAtStartup(app.Environment.EnvironmentName))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
var employeePhotoStorage = app.Services.GetRequiredService<EmployeePhotoStorageService>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = employeePhotoStorage.FileProvider,
    RequestPath = EmployeePhotoStorageService.RequestPath
});

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

// Employee pages and report downloads must be rebuilt from current master data.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            return Task.CompletedTask;
        });
    }
    await next();
});


// Account-scoped preference: shared computers must not inherit another user's language.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-IN")
    .AddSupportedCultures("en-IN", "hi-IN")
    .AddSupportedUICultures("en-IN", "hi-IN")
    .AddInitialRequestCultureProvider(new Microsoft.AspNetCore.Localization.CustomRequestCultureProvider(context =>
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var language = id is null ? "en" : context.Request.Cookies["vertex.language." + id];
        return Task.FromResult<Microsoft.AspNetCore.Localization.ProviderCultureResult?>(
            new(language == "hi" ? "hi-IN" : "en-IN"));
    })));

// ============================================================
// AUTHORIZATION
// ============================================================

app.UseAuthorization();

// ============================================================
// MVC ROUTING
// ============================================================

app.MapErpRoutes();

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
