using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VertexERP.Services;
namespace VertexERP.Controllers;
[ApiController, AllowAnonymous, Route("api/integrations/msg91/whatsapp/delivery")]
public sealed class Msg91DeliveryController(IMsg91DeliveryInbox inbox, IOptions<Msg91WhatsAppOptions> options) : ControllerBase
{
    [HttpPost, IgnoreAntiforgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Receive(CancellationToken token)
    {
        var config = options.Value;
        if (!Msg91Delivery.Authorized(config, Request.Headers[Msg91Delivery.TokenHeader].ToString())) return Unauthorized();
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(Request.Body, new JsonDocumentOptions { MaxDepth = 16 }, token); }
        catch (JsonException) { return BadRequest(); }
        using (document)
        {
            var report = Msg91Delivery.Parse(document.RootElement, config);
            if (report == null) return BadRequest();
            await inbox.ReceiveAsync(report, token);
        }
        return Ok();
    }
}
