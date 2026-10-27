using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR")]
public sealed class MyRequestsController(ApplicationDbContext db) : Controller
{
    private async Task<Employee?> CurrentEmployee()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        return await db.AppUsers.Where(x => x.Id == userId && x.IsActive)
            .Select(x => x.Employee).FirstOrDefaultAsync();
    }

    [HttpGet]
    public async Task<IActionResult> EditLeave(int id)
    {
        var employee = await CurrentEmployee();
        if (employee is null) return Forbid();
        var request = await db.LeaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
        if (request is null) return NotFound();
        if (!EmployeeRequestChanges.CanChangeLeave(request, employee.Id)) return LeaveError();
        return View(request);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLeave(int id, string leaveType, DateOnly fromDate, DateOnly toDate, string reason,
        bool isHalfDay = false, string? halfDaySession = null, TimeOnly? shortLeaveFrom = null, TimeOnly? shortLeaveTo = null)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            var employee = await CurrentEmployee();
            if (employee is null) return Forbid();
            await using var transaction = await ExtraLeaveFacility.LockAsync(db, employee.Id);
            var request = await db.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
            if (request is null) return NotFound();
            if (!EmployeeRequestChanges.CanChangeLeave(request, employee.Id)) return LeaveError();
            if (!ModelState.IsValid || fromDate.Year is < 2000 or > 2100 || toDate.Year is < 2000 or > 2100)
            {
                TempData["LeaveError"] = "Please enter a valid date, duration and reason.";
                return RedirectToAction(nameof(EditLeave), new { id });
            }
            try
            {
                EmployeeRequestChanges.UpdateLeave(request, employee, User.IsInRole("Manager") || User.IsInRole("HR"),
                    leaveType, fromDate, toDate, reason, isHalfDay, halfDaySession, shortLeaveFrom, shortLeaveTo);
            }
            catch (InvalidOperationException)
            {
                TempData["LeaveError"] = "Check your reporting manager, dates, reason and half-day session / short leave times (maximum 2 hours).";
                return RedirectToAction(nameof(EditLeave), new { id });
            }
            if (request.IsExtraFacility)
            {
                var error = await ExtraLeaveFacility.ApplicationError(db, request);
                if (error != null) { TempData["LeaveError"] = error; return RedirectToAction(nameof(EditLeave), new { id }); }
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["LeaveMessage"] = "Leave updated and sent for approval again.";
            return RedirectToAction("EmployeeLeaves", "Main");
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLeave(int id)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            var employee = await CurrentEmployee();
            if (employee is null) return Forbid();
            await using var transaction = await ExtraLeaveFacility.LockAsync(db, employee.Id);
            var request = await db.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
            if (request is null) return NotFound();
            if (!EmployeeRequestChanges.CanChangeLeave(request, employee.Id)) return LeaveError();
            db.LeaveRequests.Remove(request);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["LeaveMessage"] = "Leave request deleted successfully.";
            return RedirectToAction("EmployeeLeaves", "Main");
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditQuery(int id)
    {
        var employee = await CurrentEmployee();
        if (employee is null) return Forbid();
        var ticket = await db.QueryTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
        if (ticket is null) return NotFound();
        if (!EmployeeRequestChanges.CanEditQuery(ticket, employee.Id)) return QueryError();
        return View(ticket);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuery(int id, string subject, string category, string description)
    {
        var employee = await CurrentEmployee();
        if (employee is null) return Forbid();
        var ticket = await db.QueryTickets.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
        if (ticket is null) return NotFound();
        if (!EmployeeRequestChanges.CanEditQuery(ticket, employee.Id)) return QueryError();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 150
            || string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1500 || category?.Trim().Length > 40)
        {
            TempData["QueryError"] = "Enter a subject up to 150 characters, details up to 1500 characters and category up to 40 characters.";
            return RedirectToAction(nameof(EditQuery), new { id });
        }
        ticket.Subject = subject.Trim(); ticket.Description = description.Trim();
        ticket.Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        await db.SaveChangesAsync();
        TempData["QueryMessage"] = "Query updated successfully.";
        return RedirectToAction("EmployeeQueries", "Main");
    }

    private IActionResult LeaveError()
    {
        TempData["LeaveError"] = "Only pending leave requests can be edited or deleted. Contact HR for approved leave changes.";
        return RedirectToAction("EmployeeLeaves", "Main");
    }
    private IActionResult QueryError()
    {
        TempData["QueryError"] = "Only open queries without a response can be edited.";
        return RedirectToAction("EmployeeQueries", "Main");
    }
}
