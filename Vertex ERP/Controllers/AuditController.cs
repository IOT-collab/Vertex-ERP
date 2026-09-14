using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditController(ApplicationDbContext db) : Controller
{
    private IQueryable<AuditLog> Filter(DateOnly? from, DateOnly? to)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (from.HasValue) { var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(x => x.OccurredAtUtc >= start); }
        if (to.HasValue) { var end = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc); query = query.Where(x => x.OccurredAtUtc <= end); }
        return query;
    }

    private bool ValidDates(DateOnly? from, DateOnly? to) => ModelState.IsValid && (!from.HasValue || !to.HasValue || from <= to);

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ValidDates(from, to)) return BadRequest("Select a valid date range.");
        var query = Filter(from, to);
        var count = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(count / 100d));
        page = Math.Clamp(page, 1, pages);
        var maxId = await db.AuditLogs.MaxAsync(x => (long?)x.Id, cancellationToken) ?? 0;
        return View(new AuditLogList { From = from, To = to, Page = page, Pages = pages, Total = count, MaxId = maxId,
            Logs = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 100).Take(100).ToListAsync(cancellationToken) });
    }

    [HttpGet]
    public async Task<IActionResult> Recent(CancellationToken cancellationToken)
    {
        var logs = await db.AuditLogs.AsNoTracking().OrderByDescending(log => log.Id).Take(8).ToListAsync(cancellationToken);
        return Json(logs.Select(log => new { title = log.Action, detail = $"{log.ActorName} ({log.ActorRole}) · {log.Detail}", occurredAt = log.OccurredAtUtc }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long? id, DateOnly? from, DateOnly? to, long maxId, CancellationToken cancellationToken)
    {
        if (!ValidDates(from, to)) return BadRequest("Select a valid date range.");
        if (!id.HasValue && (!from.HasValue || !to.HasValue || maxId <= 0)) return BadRequest("Choose both dates before deleting logs.");
        var query = Filter(from, to);
        query = id.HasValue ? query.Where(x => x.Id == id.Value) : query.Where(x => x.Id <= maxId);
        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        TempData["AuditMessage"] = $"{deleted} log(s) permanently deleted.";
        return RedirectToAction(nameof(Index), new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var deleted = await db.AuditLogs.ExecuteDeleteAsync(cancellationToken);
        TempData["AuditMessage"] = $"{deleted} log(s) permanently deleted across all dates.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (!ValidDates(from, to)) return BadRequest("Select a valid date range.");
        var logs = await Filter(from, to).OrderByDescending(x => x.Id).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = new AdminReportViewModel {
            ReportTitle = "Audit Logs", FromDate = from ?? (logs.Count > 0 ? DateOnly.FromDateTime(logs.Min(x => x.OccurredAtUtc)) : today),
            ToDate = to ?? (logs.Count > 0 ? DateOnly.FromDateTime(logs.Max(x => x.OccurredAtUtc)) : today),
            FilterSummary = "Admin audit history · All matching records · Times in UTC",
            Columns = new[] { "Time (UTC)", "User", "Role", "Action", "Details" },
            Rows = logs.Select(x => (IReadOnlyList<string>)new[] { x.OccurredAtUtc.ToString("dd MMM yyyy HH:mm:ss"), x.ActorName, x.ActorRole, x.Action, x.Detail }).ToList()
        };
        return File(AdminReportPdfService.Create(report), "application/pdf", $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf");
    }
}
