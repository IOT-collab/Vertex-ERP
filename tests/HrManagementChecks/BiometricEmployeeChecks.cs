using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class BiometricEmployeeChecks
{
    public static async Task Run(ApplicationDbContext db, Action<bool, string> check)
    {
        var device = new BiometricDevice { Name = "Test", SerialNumber = "TEST-BIO", Model = "Test" };
        var other = new BiometricDevice { Name = "Other", SerialNumber = "TEST-BIO-2", Model = "Test" };
        db.BiometricDevices.AddRange(device, other);
        await db.SaveChangesAsync();
        AttendanceLog Log(int deviceId, string code) => new() { BiometricDeviceId = deviceId, DeviceUserId = code, PunchTime = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Unspecified), UniqueHash = Guid.NewGuid().ToString("N"), RawPayload = "TEST" };
        db.AttendanceLogs.AddRange(Log(device.Id, "Vpc0176"), Log(device.Id, "VPC0177"), Log(other.Id, "vpc0176"));
        await db.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetConnectionString(), x => x.EnableRetryOnFailure()).Options;
        await using var concurrent = new ApplicationDbContext(options);
        var created = await Task.WhenAll(BiometricEmployeeReconciliationService.ReconcileAsync(db), BiometricEmployeeReconciliationService.ReconcileAsync(concurrent));
        check(created.Sum() == 2, "Concurrent biometric sync creates only two ERP employees across devices");
        var first = await db.Employees.SingleAsync(x => x.EmployeeCode == "VPC0176");
        check(await db.AttendanceLogs.CountAsync(x => x.EmployeeId == first.Id) == 2 && await db.EmployeeDeviceMappings.CountAsync(x => x.EmployeeId == first.Id) == 2, "Historical punches and device identities linked to employee");
        var http = new DefaultHttpContext();
        var controller = new EmployeeController(db, null!, null!) { ControllerContext = new() { HttpContext = http }, TempData = new TempDataDictionary(http, new MemoryTempData()) };
        var directory = (EmployeeDirectoryViewModel)((ViewResult)controller.Index("VPC017", null, null)).Model!;
        var pendingDirectory = (EmployeeDirectoryViewModel)((ViewResult)controller.Index("VPC017", null, "pending")).Model!;
        check(pendingDirectory.Employees.Count == 2 && pendingDirectory.Employees.All(x => x.IsBiometricProfilePending), "HR can filter biometric profiles needing details");
        check(directory.Employees.Count == 2 && controller.Details(first.Id) is ViewResult && controller.Edit(first.Id) is ViewResult && await controller.LoginAccess(first.Id) is ViewResult, "Imported profiles support directory, view, edit and login operations");
        var form = (EmployeeFormViewModel)((ViewResult)controller.Edit(first.Id)).Model!;
        form.FirstName = "HR Edited";
        form.LastName = "Name";
        form.Department = "Production";
        form.EmployeeCode = "HR-176";
        form.PhoneNumber = "7000000176";
        form.AadhaarNumber = "123456789012";
        form.IsActive = false;
        check(await controller.Edit(first.Id, form) is RedirectToActionResult, "HR can save edits to imported employee through existing controller");
        db.AttendanceLogs.Add(Log(device.Id, "VPC0176"));
        await db.SaveChangesAsync();
        check(await BiometricEmployeeReconciliationService.ReconcileAsync(db) == 0, "Repeated sync does not duplicate edited employee");
        first = await db.Employees.SingleAsync(x => x.Id == first.Id);
        check(first.FullName == "HR Edited Name" && first.Department == "Production" && !first.IsActive && await db.AttendanceLogs.CountAsync(x => x.EmployeeId == first.Id) == 3, "HR edits, changed code and inactive state preserved while punches stay linked");
        check(await controller.DeleteConfirmed(first.Id) is RedirectToActionResult && !await db.Employees.AnyAsync(x => x.Id == first.Id), "Imported employee supports existing delete operation");
        db.AttendanceLogs.Add(Log(device.Id, "VPC0176"));
        await db.SaveChangesAsync();
        check(await BiometricEmployeeReconciliationService.ReconcileAsync(db) == 0 && !await db.Employees.AnyAsync(x => x.EmployeeCode == "VPC0176"), "Replayed punches cannot recreate HR-deleted employee");
        check(!await db.AppUsers.AnyAsync(x => x.EmployeeId == first.Id), "Biometric import does not create unsolicited login credentials");
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
