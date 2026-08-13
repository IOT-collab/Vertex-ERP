using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using VertexERP.Services;

namespace VertexERP.Controllers;

[ApiController, AllowAnonymous, Route("iclock")]
public class ZkAdmsController : ControllerBase
{
    private readonly IAttendanceSyncService _syncService;
    private readonly ILogger<ZkAdmsController> _logger;
    public ZkAdmsController(IAttendanceSyncService syncService, ILogger<ZkAdmsController> logger)
    { _syncService = syncService; _logger = logger; }

    private IActionResult? AuthorizeLanDevice()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address is null || !IsPrivateOrLoopback(address))
        {
            _logger.LogWarning("Rejected biometric request from non-LAN address {RemoteIp}", address);
            return Unauthorized("LAN DEVICE REQUIRED");
        }
        return null;
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
        return address.IsIPv6LinkLocal || (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
    }

    [HttpGet("cdata")]
    public async Task<IActionResult> Handshake([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        if (!await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken)) return Unauthorized("UNKNOWN DEVICE");
        return Content("GET OPTION FROM: " + serialNumber + "\nStamp=9999\nOpStamp=9999\nErrorDelay=60\nDelay=10\nTransTimes=00:00;14:05\nTransInterval=1\nTransFlag=1111000000\nRealtime=1\nEncrypt=0", "text/plain");
    }

    [HttpPost("cdata")]
    public async Task<IActionResult> Receive([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        try
        {
            if (Request.ContentLength is > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var reader = new StreamReader(Request.Body); var payload = await reader.ReadToEndAsync(cancellationToken);
            if (payload.Length > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            var result = await _syncService.ReceiveAsync(serialNumber ?? string.Empty, payload, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            if (!result.Accepted) return Unauthorized("UNKNOWN DEVICE");
            return Content($"OK: {result.Saved}", "text/plain");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process ADMS payload from {SerialNumber}", serialNumber);
            return StatusCode(StatusCodes.Status500InternalServerError, "ERROR");
        }
    }

    [HttpGet("getrequest")]
    public async Task<IActionResult> GetRequest([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        return await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken) ? Content("OK", "text/plain") : Unauthorized("UNKNOWN DEVICE");
    }

    [AcceptVerbs("GET", "POST"), Route("registry")]
    public async Task<IActionResult> Registry([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        if (Request.ContentLength is > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Older K40 firmware registers capabilities here before it starts polling
        // /getrequest or posting ATTLOG rows. Consume the body so the connection
        // can be reused; device metadata can be persisted in a later enhancement.
        if (Request.Method == HttpMethods.Post)
        {
            using var reader = new StreamReader(Request.Body);
            _ = await reader.ReadToEndAsync(cancellationToken);
        }

        return await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken)
            ? Content("OK", "text/plain")
            : Unauthorized("UNKNOWN DEVICE");
    }

    [AcceptVerbs("GET", "POST"), Route("test")]
    public async Task<IActionResult> Test([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            if (!await _syncService.RegisterHeartbeatAsync(serialNumber, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken))
                return Unauthorized("UNKNOWN DEVICE");
        }

        return Content("OK", "text/plain");
    }

    [AcceptVerbs("GET", "POST"), Route("devicecmd")]
    public async Task<IActionResult> DeviceCommand([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (AuthorizeLanDevice() is { } rejection) return rejection;
        _logger.LogInformation("Device command acknowledgement received from {SerialNumber}", serialNumber);
        return await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken) ? Content("OK", "text/plain") : Unauthorized("UNKNOWN DEVICE");
    }
}
