using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR,Manager")]
public sealed class AttendanceRequestsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("HR") && !User.IsInRole("Manager")) return Forbid();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
        var query = db.AttendanceRequests.AsNoTracking().Include(r => r.Employee).AsQueryable();
        if (!User.IsInRole("Admin") && !User.IsInRole("HR"))
        {
            var employeeId = await db.AppUsers.Where(u => u.Id == userId && u.IsActive).Select(u => u.EmployeeId).FirstOrDefaultAsync();
            query = User.IsInRole("Manager")
                ? query.Where(r => r.RequestedByUserId == userId || r.EmployeeId == employeeId)
                : query.Where(r => r.EmployeeId == employeeId);
        }
        return View(await query.OrderByDescending(r => r.RequestedAtUtc).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Review(int id, string decision, string? reviewReason)
    {
        if (decision is not ("Approved" or "Rejected")) return BadRequest();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Forbid();
        if (reviewReason?.Length > 300 || (decision == "Rejected" && string.IsNullOrWhiteSpace(reviewReason)))
        {
            TempData["AttendanceRequestMessage"] = "Provide a rejection reason (maximum 300 characters).";
            return RedirectToAction(nameof(Index));
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serialize manual approvals so double clicks cannot create duplicate punches or devices.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(827431902)");
        var request = await db.AttendanceRequests.Include(r => r.Employee).SingleOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound();
        if (request.Status != "Pending")
        {
            TempData["AttendanceRequestMessage"] = "This request has already been reviewed.";
            return RedirectToAction(nameof(Index));
        }
        if (decision == "Approved")
        {
            var start = request.AttendanceDate.ToDateTime(TimeOnly.MinValue);
            if (!request.Employee.IsActive || await db.AttendanceLogs.AnyAsync(l => l.EmployeeId == request.EmployeeId && l.PunchTime >= start && l.PunchTime < start.AddDays(1)))
            {
                TempData["AttendanceRequestMessage"] = "Cannot approve: employee is inactive or attendance already exists for this date. Review and reject this request if it is no longer needed.";
                return RedirectToAction(nameof(Index));
            }
            var device = await db.BiometricDevices.FirstOrDefaultAsync(d => d.SerialNumber == "MANUAL-ENTRY");
            if (device == null)
            {
                device = new BiometricDevice { Name = "ERP Manual Attendance", SerialNumber = "MANUAL-ENTRY", Model = "ERP", CommunicationMode = "Manual" };
                db.BiometricDevices.Add(device);
                await db.SaveChangesAsync();
            }
            AttendanceLog Punch(TimeOnly time, string state) => new()
            {
                BiometricDeviceId = device.Id, EmployeeId = request.EmployeeId, DeviceUserId = request.Employee.EmployeeCode,
                PunchTime = request.AttendanceDate.ToDateTime(time), PunchState = state,
                VerificationMode = "Manual Approved", UniqueHash = Guid.NewGuid().ToString("N"),
                RawPayload = $"MANUAL|Request:{request.Id}|RequestedBy:{request.RequestedByUserId}|ApprovedBy:{adminId}|Reason:{request.Reason}"
            };
            db.AttendanceLogs.Add(Punch(request.CheckInTime, "Check In"));
            if (request.CheckOutTime.HasValue) db.AttendanceLogs.Add(Punch(request.CheckOutTime.Value, "Check Out"));
        }
        request.Status = decision;
        request.ReviewedByUserId = adminId;
        request.ReviewedAtUtc = DateTime.UtcNow;
        request.ReviewReason = reviewReason?.Trim();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["AttendanceRequestMessage"] = decision == "Approved" ? "Approved. Attendance is now visible in the attendance list and employee attendance." : "Rejected. No attendance was marked.";
        return RedirectToAction(nameof(Index));
    }
}
