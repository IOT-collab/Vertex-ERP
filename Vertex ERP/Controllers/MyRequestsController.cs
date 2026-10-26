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
    public async Task<IActionResult> EditLeave(int id, string leaveType, DateOnly fromDate, DateOnly toDate, string reason, bool isHalfDay)
    {
        var employee = await CurrentEmployee();
        if (employee is null) return Forbid();
        var request = await db.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
        if (request is null) return NotFound();
        if (!EmployeeRequestChanges.CanChangeLeave(request, employee.Id)) return LeaveError();
        if (!ModelState.IsValid || !LeaveApprovalWorkflow.ValidApplication(leaveType, fromDate, toDate, reason, isHalfDay))
        {
            TempData["LeaveError"] = "Please enter a valid leave type, date range and reason.";
            return RedirectToAction(nameof(EditLeave), new { id });
        }
        var senior = User.IsInRole("Manager") || User.IsInRole("HR");
        if (!senior && !employee.ReportingManagerId.HasValue)
        {
            TempData["LeaveError"] = "Please contact HR to assign your reporting manager before applying for leave.";
            return RedirectToAction("EmployeeLeaves", "Main");
        }
        // A prior manager decision must never approve newly edited dates/reasons.
        EmployeeRequestChanges.UpdateLeave(request, employee, senior, leaveType, fromDate, toDate, reason, isHalfDay);
        await db.SaveChangesAsync();
        TempData["LeaveMessage"] = "Leave updated and sent for approval again.";
        return RedirectToAction("EmployeeLeaves", "Main");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLeave(int id)
    {
        var employee = await CurrentEmployee();
        if (employee is null) return Forbid();
        var request = await db.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == employee.Id);
        if (request is null) return NotFound();
        if (!EmployeeRequestChanges.CanChangeLeave(request, employee.Id)) return LeaveError();
        db.LeaveRequests.Remove(request);
        await db.SaveChangesAsync();
        TempData["LeaveMessage"] = "Leave request deleted successfully.";
        return RedirectToAction("EmployeeLeaves", "Main");
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
