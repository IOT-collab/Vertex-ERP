using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VertexERP.Data;
using VertexERP.Repositories;
using VertexERP.Services;

using var instanceMutex = new Mutex(true, @"Local\VertexERP.BiometricReceiver", out var ownsInstanceMutex);
if (!ownsInstanceMutex)
    return;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("remoteattendance.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8082));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));
builder.Services.AddScoped<IBiometricRepository, BiometricRepository>();
builder.Services.AddScoped<IAttendanceSyncService, AttendanceSyncService>();
builder.Services.Configure<RemoteAttendanceOptions>(builder.Configuration.GetSection(RemoteAttendanceOptions.SectionName));
builder.Services.AddHttpClient(RemoteAttendanceImportService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer(),
    AllowAutoRedirect = true
});
builder.Services.AddHostedService<RemoteAttendanceImportService>();

var app = builder.Build();
var auditDirectory = Path.Combine(app.Environment.ContentRootPath, "logs");
var auditLogPath = Path.Combine(auditDirectory, "incoming-requests.log");
Directory.CreateDirectory(auditDirectory);

app.Use(async (context, next) =>
{
    var startedAt = DateTime.UtcNow;
    try
    {
        await next();
        await AppendAuditAsync(auditLogPath,
            $"{startedAt:O}\t{GetSourceIp(context) ?? "unknown"}\t{context.Request.Method}\t{context.Request.Path}{context.Request.QueryString}\t{context.Response.StatusCode}");
    }
    catch (Exception exception)
    {
        await AppendAuditAsync(auditLogPath,
            $"{startedAt:O}\t{GetSourceIp(context) ?? "unknown"}\t{context.Request.Method}\t{context.Request.Path}{context.Request.QueryString}\tERROR\t{exception.GetType().Name}: {exception.Message}");
        throw;
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException("Biometric receiver could not connect to the VertexERP database.");
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "Vertex ERP Biometric Receiver",
    status = "running",
    port = 8082,
    timeUtc = DateTime.UtcNow
}));

app.MapGet("/iclock/cdata", async (HttpContext context, IAttendanceSyncService sync, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!IsLanRequest(context)) return Results.Unauthorized();
    var serial = GetSerial(context);
    logger.LogInformation("ADMS handshake from {Ip} serial {Serial}", GetSourceIp(context), serial);
    if (!await sync.RegisterHeartbeatAsync(serial, GetSourceIp(context), cancellationToken))
        return Results.Text("UNKNOWN DEVICE", "text/plain", statusCode: StatusCodes.Status401Unauthorized);

    var response = $"GET OPTION FROM: {serial}\n" +
                   "Stamp=0\nOpStamp=0\nErrorDelay=10\nDelay=5\n" +
                   "TransTimes=00:00;14:05\nTransInterval=1\n" +
                   "TransFlag=1111000000\nRealtime=1\nEncrypt=0";
    return Results.Text(response, "text/plain");
});

app.MapPost("/iclock/cdata", async (HttpContext context, IAttendanceSyncService sync, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!IsLanRequest(context)) return Results.Unauthorized();
    if (context.Request.ContentLength is > 1_048_576)
        return Results.Text("PAYLOAD TOO LARGE", "text/plain", statusCode: StatusCodes.Status413PayloadTooLarge);

    using var reader = new StreamReader(context.Request.Body);
    var payload = await reader.ReadToEndAsync(cancellationToken);
    if (payload.Length > 1_048_576)
        return Results.Text("PAYLOAD TOO LARGE", "text/plain", statusCode: StatusCodes.Status413PayloadTooLarge);

    var serial = GetSerial(context);
    var result = await sync.ReceiveAsync(serial, payload, GetSourceIp(context), cancellationToken);
    await AppendAuditAsync(auditLogPath,
        $"{DateTime.UtcNow:O}\tATTLOG\tSN={serial}\treceived={result.Received}\tsaved={result.Saved}\tunmapped={result.Unmapped}\taccepted={result.Accepted}");
    logger.LogInformation("ADMS ATTLOG from {Ip} serial {Serial}: received {Received}, saved {Saved}",
        GetSourceIp(context), serial, result.Received, result.Saved);
    return result.Accepted
        ? Results.Text($"OK: {result.Saved}", "text/plain")
        : Results.Text("UNKNOWN DEVICE", "text/plain", statusCode: StatusCodes.Status401Unauthorized);
});

app.MapMethods("/iclock/registry", [HttpMethods.Get, HttpMethods.Post], HandleHeartbeatAsync);
app.MapMethods("/iclock/getrequest", [HttpMethods.Get], HandleHeartbeatAsync);
app.MapMethods("/iclock/devicecmd", [HttpMethods.Get, HttpMethods.Post], HandleHeartbeatAsync);
app.MapMethods("/iclock/test", [HttpMethods.Get, HttpMethods.Post], async (HttpContext context, IAttendanceSyncService sync, CancellationToken cancellationToken) =>
{
    if (!IsLanRequest(context)) return Results.Unauthorized();
    var serial = GetSerial(context);
    if (serial.Length > 0 && !await sync.RegisterHeartbeatAsync(serial, GetSourceIp(context), cancellationToken))
        return Results.Text("UNKNOWN DEVICE", "text/plain", statusCode: StatusCodes.Status401Unauthorized);
    return Results.Text("OK", "text/plain");
});

app.Run();

static async Task<IResult> HandleHeartbeatAsync(HttpContext context, IAttendanceSyncService sync, ILogger<Program> logger, CancellationToken cancellationToken)
{
    if (!IsLanRequest(context)) return Results.Unauthorized();
    var serial = GetSerial(context);
    logger.LogInformation("ADMS {Path} from {Ip} serial {Serial}", context.Request.Path, GetSourceIp(context), serial);
    return await sync.RegisterHeartbeatAsync(serial, GetSourceIp(context), cancellationToken)
        ? Results.Text("OK", "text/plain")
        : Results.Text("UNKNOWN DEVICE", "text/plain", statusCode: StatusCodes.Status401Unauthorized);
}

static string GetSerial(HttpContext context) => context.Request.Query["SN"].ToString().Trim().ToUpperInvariant();

static string? GetSourceIp(HttpContext context)
{
    var address = context.Connection.RemoteIpAddress;
    if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
    return address?.ToString();
}

static bool IsLanRequest(HttpContext context)
{
    var address = context.Connection.RemoteIpAddress;
    if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
    if (address is null) return false;
    if (IPAddress.IsLoopback(address)) return true;
    var bytes = address.GetAddressBytes();
    return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
           (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 169 && bytes[1] == 254));
}

static async Task AppendAuditAsync(string path, string message)
{
    await File.AppendAllTextAsync(path, message + Environment.NewLine);
}
