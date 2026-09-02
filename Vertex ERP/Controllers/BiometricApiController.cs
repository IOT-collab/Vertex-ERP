using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[ApiController, AllowAnonymous, Route("api/biometric/v1")]
[RequestSizeLimit(1_048_576)]
public sealed class BiometricApiController : ControllerBase
{
    private readonly IAttendanceSyncService _sync;
    public BiometricApiController(IAttendanceSyncService sync) => _sync = sync;

    [HttpGet("health")]
    public IActionResult Health() => IsLanRequest() ? Ok(new { status = "ready", apiVersion = "v1", serverTimeUtc = DateTime.UtcNow }) : Unauthorized();

    [HttpPost("punches")]
    public async Task<ActionResult<BiometricPunchBatchResponse>> Receive(BiometricPunchBatchRequest request, CancellationToken cancellationToken)
    {
        if (!IsLanRequest()) return Unauthorized(new { message = "Biometric API is available only on the private LAN." });
        var punches = request.Punches.Select(item => new NormalizedBiometricPunch(item.DeviceUserId, item.PunchTime, item.PunchState, item.VerificationMode, item.WorkCode, item.EventId)).ToList();
        var result = await _sync.ReceiveNormalizedAsync(request.DeviceSerialNumber, punches, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        var response = new BiometricPunchBatchResponse(result.Accepted, result.Received, result.Saved, result.Unmapped, result.Message);
        return result.Accepted ? Ok(response) : Unauthorized(response);
    }

    // Used only by the on-premise receiver. ADMS devices must continue to send
    // their proprietary protocol to the local receiver, never to Azure directly.
    [HttpPost("adms")]
    public async Task<IActionResult> ReceiveAdms([FromQuery(Name = "SN")] string? serialNumber, CancellationToken cancellationToken)
    {
        if (!HasValidGatewayKey()) return Unauthorized(new { message = "Invalid biometric gateway key." });
        if (Request.ContentLength is > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        if (payload.Length > 1_048_576) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var result = await _sync.ReceiveAsync(serialNumber ?? string.Empty, payload,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Accepted
            ? Ok(new { result.Received, result.Saved, result.Unmapped })
            : Unauthorized(new { result.Message });
    }

    private bool HasValidGatewayKey()
    {
        var configuredKey = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["BiometricIngress:ApiKey"];
        var suppliedKey = Request.Headers["X-Biometric-Gateway-Key"].ToString();
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(suppliedKey)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configuredKey), Encoding.UTF8.GetBytes(suppliedKey));
    }

    private bool IsLanRequest()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address is null || IPAddress.IsLoopback(address)) return address is not null;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
        return address.IsIPv6LinkLocal || (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
    }
}
