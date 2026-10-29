using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
namespace Vertex_ERP.Controllers;

[Authorize(Roles = "HR,Admin,Manager,Employee,User")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(NotificationInboxService inbox, ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? category, bool unread = false, int page = 1, CancellationToken cancellationToken = default)
    {
        if ((await inbox.ViewerIdAsync(User, cancellationToken)) == null) return Forbid();
        var items = await inbox.LoadAsync(User, cancellationToken);
        var filtered = items.Where(x => (string.IsNullOrEmpty(category) || x.Category == category) && (!unread || !x.IsRead)).ToList();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(filtered.Count / 30d)));
        return View(new NotificationInboxViewModel { Items = filtered.Skip((page - 1) * 30).Take(30).ToList(), UnreadCount = items.Count(x => !x.IsRead), Total = filtered.Count, Category = category, UnreadOnly = unread, Page = page });
    }
    [HttpGet]
    public async Task<IActionResult> Count(CancellationToken cancellationToken)
    {
        if ((await inbox.ViewerIdAsync(User, cancellationToken)) == null) return Forbid();
        var items = await inbox.LoadAsync(User, cancellationToken);
        return Json(new { count = items.Count(x => !x.IsRead) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(string key, CancellationToken cancellationToken)
    {
        var userId = await inbox.ViewerIdAsync(User, cancellationToken);
        if (!userId.HasValue) return Forbid();
        var item = (await inbox.LoadAsync(User, cancellationToken)).FirstOrDefault(x => x.Key == key);
        if (item == null)
        {
            TempData["NotificationMessage"] = "This request has changed or is no longer available. Your latest notifications are shown below.";
            return RedirectToAction(nameof(Index));
        }
        await inbox.MarkReadAsync(userId.Value, [item.Key], cancellationToken);
        return LocalRedirect(item.Url);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(string? key, bool all = false, bool unread = false, CancellationToken cancellationToken = default)
    {
        var userId = await inbox.ViewerIdAsync(User, cancellationToken);
        if (!userId.HasValue) return Forbid();
        var items = await inbox.LoadAsync(User, cancellationToken);
        var keys = items.Where(x => all ? (unread || !x.IsRead) : x.Key == key).Select(x => x.Key).ToList();
        if (!all && keys.Count == 0) return RedirectToAction(nameof(Index));
        if (unread) await db.NotificationReadReceipts.Where(x => x.UserId == userId && keys.Contains(x.EventKey)).ExecuteDeleteAsync(cancellationToken);
        else await inbox.MarkReadAsync(userId.Value, keys, cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}
