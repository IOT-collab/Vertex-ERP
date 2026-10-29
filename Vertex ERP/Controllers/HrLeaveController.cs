using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
namespace Vertex_ERP.Controllers;

[Authorize(Roles="HR,Admin")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class HrLeaveController(ApplicationDbContext db):Controller
{
    private bool CanSubmit=>User.IsInRole("HR") && !User.IsInRole("Admin");
    private async Task<HrLeavePage> PageAsync(HrLeaveApplication? form=null) => new() {
        CanSubmit=CanSubmit,Input=form??new(),
        Employees=CanSubmit ? await db.Employees.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.FullName)
            .Select(x=>new HrLeaveEmployeeOption(x.Id,x.FullName,x.EmployeeCode,x.Department,x.ReportingManager!=null?x.ReportingManager.FullName:null)).ToListAsync() : [],
        Requests=await db.LeaveRequests.AsNoTracking().Where(x=>x.SubmittedByHrUserId!=null).Include(x=>x.Employee).Include(x=>x.AssignedApproverEmployee)
            .OrderByDescending(x=>x.AppliedAtUtc).Take(100).ToListAsync()
    };
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if(!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out _)) return Forbid();
        return View(await PageAsync());
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="HR")]
    public async Task<IActionResult> Apply(HrLeaveApplication form)
    {
        if(!CanSubmit || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var actorId)) return Forbid();
        var actor=await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==actorId && x.IsActive);
        if(actor==null || AccountRoleService.Normalize(actor.Role)!=AccountRoleService.HR) return Forbid();
        if(!ModelState.IsValid || !HrLeaveApplications.Valid(form)) {
            ModelState.AddModelError("","Select an employee, valid leave dates (up to 366 days), leave type and reason. Half day/short leave must be for a single date.");
            return View("Index",await PageAsync(form));
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async()=> {
            db.ChangeTracker.Clear();
            await using var transaction=await ExtraLeaveFacility.LockAsync(db,form.EmployeeId);
            var employee=await db.Employees.AsNoTracking().Include(x=>x.ReportingManager).SingleOrDefaultAsync(x=>x.Id==form.EmployeeId && x.IsActive);
            string? error=null;
            if(employee==null) error="Select an active employee.";
            else if(actor.EmployeeId==employee.Id) error="Use My Leave to submit your own leave request.";
            else if(employee.ReportingManagerId==null || employee.ReportingManagerId==employee.Id || employee.ReportingManager?.IsActive!=true) error="Assign an active reporting manager before applying.";
            else {
                var accounts=await db.AppUsers.AsNoTracking().Where(x=>x.EmployeeId==employee.ReportingManagerId && x.IsActive).ToListAsync();
                if(!accounts.Any(x=>AccountRoleService.EffectiveRole(x.Role,true)==AccountRoleService.Manager)) error="The reporting manager needs an active manager login.";
            }
            if(error!=null) { ModelState.AddModelError("",error);return View("Index",await PageAsync(form)); }
            var type=ExtraLeaveFacility.IsFacilityType(form.LeaveType)?form.LeaveType:form.LeaveType.Trim().ToUpperInvariant();
            var request=new LeaveRequest { EmployeeId=employee!.Id,LeaveType=type,FromDate=form.FromDate!.Value,ToDate=form.ToDate!.Value,Reason=form.Reason.Trim(),
                ApprovalLevel="Manager",AssignedApproverEmployeeId=employee.ReportingManagerId,SubmittedByHrUserId=actor.Id,SubmittedByHrName=actor.FullName };
            ExtraLeaveFacility.SetDetails(request,type,form.HalfDaySession,form.ShortLeaveFrom,form.ShortLeaveTo);
            error=await HrLeaveApplications.ErrorAsync(db,request);
            if(error!=null) { ModelState.AddModelError("",error);return View("Index",await PageAsync(form)); }
            db.LeaveRequests.Add(request);await db.SaveChangesAsync();await transaction.CommitAsync();
            TempData["HrLeaveMessage"]="Leave applied on the employee's behalf and sent to their reporting manager. HR final approval will follow.";
            return RedirectToAction(nameof(Index));
        });
    }
}
