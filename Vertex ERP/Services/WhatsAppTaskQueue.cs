using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VertexERP.Data;
using VertexERP.Models;
namespace VertexERP.Services;

public interface IWhatsAppTaskQueue
{
    Task<WhatsAppTaskNotification?> ClaimAsync(CancellationToken token);
    Task<TaskWhatsAppData?> LoadAsync(WhatsAppTaskNotification item, CancellationToken token);
    Task CompleteAsync(WhatsAppTaskNotification item, Msg91Result result, CancellationToken token);
}

public sealed class WhatsAppTaskQueue(ApplicationDbContext db) : IWhatsAppTaskQueue
{
    public async Task<WhatsAppTaskNotification?> ClaimAsync(CancellationToken token)
    {
        var now = DateTime.UtcNow;
        // A crashed worker may have already sent its request. Never automatically resend it.
        await db.WhatsAppTaskNotifications.Where(x => x.Status == "Processing" && x.LastAttemptAtUtc < now.AddMinutes(-5))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "NeedsReview").SetProperty(x => x.ErrorCode, "worker_interrupted_outcome_unknown"), token);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var item = await db.WhatsAppTaskNotifications.FromSqlInterpolated($"SELECT * FROM \"WhatsAppTaskNotifications\" WHERE \"Status\" IN ('Pending','Retry') AND \"NextAttemptAtUtc\" <= {now} ORDER BY \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").FirstOrDefaultAsync(token);
            if (item != null) { item.Status = "Processing"; item.Attempts++; item.LastAttemptAtUtc = now; await db.SaveChangesAsync(token); }
            await transaction.CommitAsync(token);
            return item;
        });
    }
    public async Task<TaskWhatsAppData?> LoadAsync(WhatsAppTaskNotification item, CancellationToken token)
    {
        return await db.WorkTasks.AsNoTracking().Where(t => t.Id == item.TaskId && t.AssigneeId == item.AssigneeId && t.Assignee.IsActive)
            .Select(t => new TaskWhatsAppData(t.Assignee.FullName, t.Assignee.PhoneNumber, t.Title, t.Manager.FullName, t.DueDate)).SingleOrDefaultAsync(token);
    }
    public async Task CompleteAsync(WhatsAppTaskNotification item, Msg91Result result, CancellationToken token)
    {
        ApplyResult(item, result);
        await db.SaveChangesAsync(token);
    }
    public static void ApplyResult(WhatsAppTaskNotification item, Msg91Result result)
    {
        item.Status = result.Outcome; item.HttpStatus = result.HttpStatus; item.ProviderMessageId = result.ProviderId; item.ErrorCode = result.ErrorCode;
        if (item.Status == "Retry")
        {
            if (item.Attempts >= 5) { item.Status = "Failed"; item.ErrorCode = "retry_limit_reached"; }
            else item.NextAttemptAtUtc = DateTime.UtcNow + RetryDelay(item.Attempts, result.RetryAfter);
        }
        if (item.Status == "Disabled") { item.Status = "Pending"; item.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(1); }
    }
    public static TimeSpan RetryDelay(int attempts, TimeSpan? retryAfter) =>
        TimeSpan.FromSeconds(Math.Clamp(Math.Max(30 * Math.Pow(2, Math.Min(attempts, 8)) + Random.Shared.Next(0, 10), retryAfter?.TotalSeconds ?? 0), 30, 86400));
}

public sealed class WhatsAppTaskDispatcher(IWhatsAppTaskQueue queue, IMsg91WhatsAppService sender, IOptions<Msg91WhatsAppOptions> options, ILogger<WhatsAppTaskDispatcher> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken token)
    {
        if (!options.Value.Ready) return false;
        var item = await queue.ClaimAsync(token);
        if (item == null) return false;
        var data = await queue.LoadAsync(item, token);
        if (data != null && Msg91WhatsAppService.NormalizePhone(data.Phone) is string phone) item.RecipientNumberHash = Msg91Delivery.Hash(phone);
        var result = data == null ? new Msg91Result("Skipped", ErrorCode: "task_deleted_reassigned_or_inactive") : await sender.SendTaskAsync(data, token);
        await queue.CompleteAsync(item, result, token);
        logger.LogInformation("MSG91 task notification {NotificationId}: {Status}; HTTP {HttpStatus}; SubmittedAtUtc {SubmittedAtUtc}; ProviderId {ProviderId}; ProviderResponse {ProviderResponse}; Error {ErrorCode}",
            item.Id, item.Status, item.HttpStatus, result.SubmittedAtUtc, item.ProviderMessageId, result.SanitizedResponse, item.ErrorCode);
        return true;
    }
}

public sealed class WhatsAppTaskWorker(IServiceScopeFactory scopes, ILogger<WhatsAppTaskWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool? lastReadyState = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<Msg91WhatsAppOptions>>().Value;
                if (lastReadyState != config.Ready)
                {
                    if (config.Ready)
                        logger.LogInformation("MSG91 task notifications are enabled; worker will process the durable queue.");
                    else
                        logger.LogWarning("MSG91 task notifications are paused; pending records remain queued. ReadinessIssues={ReadinessIssues}.",
                            string.Join(',', config.ReadinessIssues));
                    lastReadyState = config.Ready;
                }
                if (config.WebhooksEnabled) await scope.ServiceProvider.GetRequiredService<Msg91DeliveryInbox>().ReconcileAsync(stoppingToken);
                if (await scope.ServiceProvider.GetRequiredService<WhatsAppTaskDispatcher>().ProcessNextAsync(stoppingToken)) continue; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError("MSG91 queue processing failed ({ExceptionType}). Pending work remains stored; processing will resume.", exception.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
