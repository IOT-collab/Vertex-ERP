using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VertexERP.Controllers;
using VertexERP.Models;
using VertexERP.Services;

static class DeliveryChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var config = new Msg91WhatsAppOptions { SendingEnabled = true, AuthKey = "FAKE-KEY", WebhooksEnabled = true, WebhookToken = new string('a',64) };
        check(config.TemplateMappingVerified && config.Ready && config.ApprovedTemplateText == Msg91WhatsAppOptions.ConfirmedTemplateBody, "Confirmed template is configured; live approval remains a separate switch");
        check(config.ParameterOrder.SequenceEqual(new[] { "EmployeeName", "TaskTitle", "AssignedBy", "TaskDeadline" }), "Confirmed approved template order");
        check(!new Msg91WhatsAppOptions().Ready && !new Msg91WhatsAppOptions().WebhooksEnabled, "Sending and callback processing remain off by default");
        check(!Msg91Delivery.Authorized(config, "wrong") && !Msg91Delivery.Authorized(config, null), "Webhook requires independent secret");
        check(Msg91Delivery.Authorized(config, new string('a',64)), "Valid configured webhook token accepted");
        var json = """
        {"requestId":"bulk-test-123","customerNumber":"919876543210","eventName":"delivered","integratedNumber":"919217614338","direction":"1","templateName":"vertex_task_assigned","templateLanguage":"en","uuid":"wamid.test","ts":"2026-10-29T13:00:00+05:30","reason":""}
        """;
        WhatsAppDeliveryEvent? Parse(string text) { using var d = JsonDocument.Parse(text); return Msg91Delivery.Parse(d.RootElement,config); }
        var report = Parse(json)!;
        check(report.Status == "Delivered" && report.OccurredAtUtc == new DateTime(2026,10,29,7,30,0,DateTimeKind.Utc), "Documented MSG91 callback fields and explicit-offset timestamp parsed");
        check(report.RecipientNumberHash == Msg91Delivery.Hash("919876543210") && !report.EventKey.Contains("919876543210"), "Callback retains a recipient hash rather than phone/content");
        check(Parse(json)!.EventKey == report.EventKey, "Provider callback replay has same idempotency key");
        foreach (var (before,after) in new[] { ("\"direction\":\"1\"", "\"direction\":\"0\""), ("919217614338","919999999999"), ("vertex_task_assigned","other_template"), ("\"en\"","\"fr\""), ("\"delivered\"","\"unknown\""), ("2026-10-29T13:00:00+05:30","2026-10-29T13:00:00"), ("bulk-test-123", "") })
            check(Parse(json.Replace(before,after)) == null, "Reject unrelated or malformed callback: " + before);
        var item = new WhatsAppTaskNotification { Status = "Accepted" };
        Msg91Delivery.Apply(item,report);
        check(item.Status == "Accepted" && item.DeliveryStatus == "Delivered" && item.MetaMessageId == "wamid.test" && item.DeliveredAtUtc == report.OccurredAtUtc, "Delivery tracking separate from send acceptance and records Meta ID");
        Msg91Delivery.Apply(item,Parse(json.Replace("delivered","sent"))!);
        check(item.DeliveryStatus == "Delivered", "Delayed Sent cannot downgrade Delivered");
        Msg91Delivery.Apply(item,Parse(json.Replace("delivered","read"))!);
        Msg91Delivery.Apply(item,report);
        check(item.DeliveryStatus == "Read" && item.ReadAtUtc.HasValue, "Read never regresses on duplicate or older delivery event");
        var failed = Parse(json.Replace("delivered","failed").Replace("\"reason\":\"\"", "\"reason\":\"131049: private content must not be logged\""))!;
        check(failed.ErrorCode == "msg91_131049", "Only sanitized provider error code retained");
        var failedItem = new WhatsAppTaskNotification(); Msg91Delivery.Apply(failedItem,failed);
        Msg91Delivery.Apply(failedItem,Parse(json.Replace("delivered","sent"))!);
        check(failedItem.DeliveryStatus == "Failed", "Delayed Sent cannot replace terminal failure");
        Msg91Delivery.Apply(failedItem,report);
        check(failedItem.DeliveryStatus == "Delivered" && failedItem.DeliveryErrorCode == null, "Positive delivery evidence supersedes failure");
        var sink = new Inbox();
        Msg91DeliveryController Controller(string? secret, string body)
        {
            var context = new DefaultHttpContext(); context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            if (secret != null) context.Request.Headers[Msg91Delivery.TokenHeader] = secret;
            return new(sink,Options.Create(config)) { ControllerContext = new ControllerContext { HttpContext = context } };
        }
        check(await Controller(null,json).Receive(default) is UnauthorizedResult && sink.Events.Count == 0, "Unauthorized callback cannot write to inbox");
        check(await Controller(config.WebhookToken,"bad-json").Receive(default) is BadRequestResult && sink.Events.Count == 0, "Malformed callback returns 400 without storing data");
        check(await Controller(config.WebhookToken,json).Receive(default) is OkResult && sink.Events.Count == 1, "Authorized documented callback accepted into durable inbox abstraction");
        check(await Controller(config.WebhookToken,json).Receive(default) is OkResult && sink.Events.Count == 1, "Repeated webhook acknowledged without duplicate inbox entry");
    }
    sealed class Inbox : IMsg91DeliveryInbox
    {
        public Dictionary<string,WhatsAppDeliveryEvent> Events = new();
        public Task ReceiveAsync(WhatsAppDeliveryEvent report, CancellationToken token) { Events.TryAdd(report.EventKey,report); return Task.CompletedTask; }
    }
}
