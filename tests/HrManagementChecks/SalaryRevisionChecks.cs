using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class SalaryRevisionChecks
{
    public static async Task Run(ApplicationDbContext db, int employeeId, int userId, Action<bool,string> check)
    {
        var slip = await db.GeneratedSalarySlips.AsNoTracking().SingleAsync(s => s.EmployeeId == employeeId);
        SalarySlipRevisionController Controller()
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,userId.ToString()), new Claim(ClaimTypes.Name,"Test HR"), new Claim(ClaimTypes.Role,"HR") }, "test")) };
            return new(db) { ControllerContext = new() { HttpContext = http }, TempData = new TempDataDictionary(http,new MemoryTempData()) };
        }
        async Task<SalarySlipRevisionViewModel> Model() => (SalarySlipRevisionViewModel)((ViewResult)await Controller().Edit(slip.Id)).Model!;
        var invalid = await Model(); invalid.Reason = "";
        check(await Controller().Edit(invalid) is ViewResult && !await db.AuditLogs.AnyAsync(a => a.EntityType == "SalarySlipRevision:" + slip.Id), "Revision requires reason and does not alter history on failure");
        invalid = await Model(); invalid.Reason = "Bad days"; invalid.Values["SalaryDays"] = 32;
        check(await Controller().Edit(invalid) is ViewResult, "Revision rejects days outside selected month");
        invalid = await Model(); invalid.Reason = "Bad deductions"; invalid.Values["LeaveDeduction"] = 999999;
        check(await Controller().Edit(invalid) is ViewResult, "Revision rejects deductions above gross salary");
        var stale = await Model(); stale.Reason = "Stale tab";
        var correction = await Model(); correction.Reason = "Correct salary days";
        correction.Values["SalaryDays"] = DateTime.DaysInMonth(slip.Year,slip.Month);
        check(await Controller().Edit(correction) is RedirectToActionResult, "Existing salary slip can be revised");
        db.ChangeTracker.Clear();
        var saved = await db.GeneratedSalarySlips.AsNoTracking().SingleAsync(s => s.Id == slip.Id);
        check(saved.SalaryDays == correction.Values["SalaryDays"] && saved.NetSalary == slip.NetSalary && saved.GeneratedAtUtc == slip.GeneratedAtUtc,
            "Days correction preserves salary amount and original generation date");
        var audit = await db.AuditLogs.SingleAsync(a => a.EntityType == "SalarySlipRevision:" + slip.Id);
        check(audit.ActorId == userId.ToString() && audit.Detail.Contains("Correct salary days") && audit.Detail.Contains("Before") && audit.Detail.Contains("After"), "Revision stores reason, actor and before/after values atomically");
        check(await Controller().Edit(stale) is ViewResult && await db.AuditLogs.CountAsync(a => a.EntityType == "SalarySlipRevision:" + slip.Id) == 1,
            "Stale revision cannot overwrite another correction");
        var money = await Model(); money.Reason = "Correct deduction"; money.Values["LeaveDeduction"] = 0;
        check(await Controller().Edit(money) is RedirectToActionResult, "Salary deduction can be corrected separately");
        db.ChangeTracker.Clear();
        saved = await db.GeneratedSalarySlips.AsNoTracking().SingleAsync(s => s.Id == slip.Id);
        check(saved.NetSalary == slip.NetSalary + slip.LeaveDeduction && await db.GeneratedSalarySlips.CountAsync() == 2,
            "Deduction correction updates net salary without duplicate slips");
        var pdf = SalarySlipPdfService.Create(await db.Employees.FindAsync(employeeId) ?? throw new Exception(),saved,null);
        check(System.Text.Encoding.ASCII.GetString(pdf).Contains("(" + saved.SalaryDays?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")"), "Corrected PDF uses revised salary days");
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context) => new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values) { }
    }
}
