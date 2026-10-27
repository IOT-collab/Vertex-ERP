using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize]
public sealed class AttendanceRequestsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var canMark = await new ManualAttendanceAccess(db).CanMarkAsync(User);
        ViewData["CanMarkManualAttendance"] = canMark;
        if (!canMark && !User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
        var query = db.AttendanceRequests.AsNoTracking().Include(r => r.Employee).AsQueryable();
        if (!canMark && !User.IsInRole("Admin"))
        {
            var employeeId = await db.AppUsers.Where(u => u.Id == userId && u.IsActive).Select(u => u.EmployeeId).FirstOrDefaultAsync();
            query = User.IsInRole("Manager")
                ? query.Where(r => r.RequestedByUserId == userId || r.EmployeeId == employeeId)
                : query.Where(r => r.EmployeeId == employeeId);
        }
        var requests = await query.OrderByDescending(r => r.RequestedAtUtc).ToListAsync();
        var actorIds = requests.Select(x => x.RequestedByUserId).ToList();
        ViewData["AttendanceActors"] = await db.AppUsers.AsNoTracking().Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName);
        return View(requests);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Review(int id, string decision, string? reviewReason)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            if (decision is not ("Approved" or "Rejected")) return BadRequest();
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();
            if (reviewReason?.Length > 300 || (decision == "Rejected" && string.IsNullOrWhiteSpace(reviewReason)))
            {
                TempData["AttendanceRequestMessage"] = "Provide a rejection reason (maximum 300 characters).";
                return RedirectToAction(nameof(Index));
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            await ManualAttendance.LockAsync(db);
            var request = await db.AttendanceRequests.Include(r => r.Employee).SingleOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();
            if (request.Status != "Pending")
            {
                TempData["AttendanceRequestMessage"] = "This request has already been reviewed.";
                return RedirectToAction(nameof(Index));
            }
            if (decision == "Approved")
            {
                var error = await ManualAttendance.MarkAsync(db, request.Employee, request.AttendanceDate,
                    request.CheckInTime, request.CheckOutTime, request.Reason, adminId, request.Id);
                if (error != null) { TempData["AttendanceRequestMessage"] = error; return RedirectToAction(nameof(Index)); }
            }
            request.Status = decision;
            request.ReviewedByUserId = adminId;
            request.ReviewedAtUtc = DateTime.UtcNow;
            request.ReviewReason = reviewReason?.Trim();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["AttendanceRequestMessage"] = decision == "Approved" ? "Approved. Attendance is now visible in the attendance list and employee attendance." : "Rejected. No attendance was marked.";
            return RedirectToAction(nameof(Index));
        });
    }
}
