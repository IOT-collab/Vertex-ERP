using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Services;

namespace VertexERP.Controllers;

[ApiController, AllowAnonymous, Route("iclock")]
public class ZkAdmsController : ControllerBase
{
    private readonly IAttendanceSyncService _syncService;
    private readonly ILogger<ZkAdmsController> _logger;
    public ZkAdmsController(IAttendanceSyncService syncService, ILogger<ZkAdmsController> logger) { _syncService = syncService; _logger = logger; }

    [HttpGet("cdata")]
    public async Task<IActionResult> Handshake([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (!await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken)) return Unauthorized("UNKNOWN DEVICE");
        return Content("GET OPTION FROM: " + serialNumber + "\nStamp=9999\nOpStamp=9999\nErrorDelay=60\nDelay=10\nTransTimes=00:00;14:05\nTransInterval=1\nTransFlag=1111000000\nRealtime=1\nEncrypt=0", "text/plain");
    }

    [HttpPost("cdata")]
    public async Task<IActionResult> Receive([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(Request.Body); var payload = await reader.ReadToEndAsync(cancellationToken);
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
    public async Task<IActionResult> GetRequest([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken) =>
        await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken) ? Content("OK", "text/plain") : Unauthorized("UNKNOWN DEVICE");

    [HttpPost("devicecmd")]
    public async Task<IActionResult> DeviceCommand([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Device command acknowledgement received from {SerialNumber}", serialNumber);
        return await _syncService.RegisterHeartbeatAsync(serialNumber ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken) ? Content("OK", "text/plain") : Unauthorized("UNKNOWN DEVICE");
    }
}
