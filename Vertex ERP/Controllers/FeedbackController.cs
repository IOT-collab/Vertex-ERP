using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeedbackController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "FeedbackDocuments");

    [HttpGet]
    public IActionResult Message() => View(new FeedbackForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(FeedbackForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Message", form);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
        var user = await db.AppUsers.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null) return Forbid();
        db.EmployeeFeedbacks.Add(new EmployeeFeedback
        {
            SubmittedByUserId = user.Id, SenderName = user.FullName, SenderRole = user.Role,
            EmployeeCode = user.Employee?.EmployeeCode, Department = user.Employee?.Department,
            Subject = form.Subject.Trim(), Message = form.Message.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
        TempData["FeedbackMessage"] = "Your feedback has been submitted to HR and Admin.";
        return RedirectToAction(nameof(Message));
    }

    [HttpGet, Authorize(Roles = "HR,Admin")]
    public IActionResult Inbox() => RedirectToAction("Responses", "FeedbackDocuments");

    [HttpGet, Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> MessageInbox(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 150) return BadRequest();
        var query = db.EmployeeFeedbacks.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            var term = search.ToLower();
            query = query.Where(x => x.SenderName.ToLower().Contains(term) || (x.EmployeeCode != null && x.EmployeeCode.ToLower().Contains(term))
                || x.Subject.ToLower().Contains(term) || (x.Department != null && x.Department.ToLower().Contains(term)));
        }
        var total = await query.CountAsync(cancellationToken);
        const int pageSize = 25;
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return View(new FeedbackInboxViewModel { Items = items, Page = page, Total = total, PageSize = pageSize, Search = search });
    }
}
