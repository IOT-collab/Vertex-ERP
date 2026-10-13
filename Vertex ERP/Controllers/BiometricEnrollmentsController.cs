using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public sealed class BiometricEnrollmentsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Employees = await db.Employees.AsNoTracking().OrderBy(x => x.FullName).ToListAsync();
        return View(await db.BiometricEnrollments.AsNoTracking().Include(x => x.Employee).OrderBy(x => x.FullName).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(string enrollCode, int employeeId, bool confirmed)
    {
        if (!confirmed || employeeId < 0 || string.IsNullOrWhiteSpace(enrollCode)) return BadRequest();
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            return await LinkWithinTransactionAsync(enrollCode, employeeId);
        });
        if (result is not null) return result;
        await BiometricEmployeeReconciliationService.ReconcileAsync(db);
        TempData["EnrollmentMessage"] = "Enrollment verified. Future and unassigned punches use this link; previously assigned attendance is unchanged.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult?> LinkWithinTransactionAsync(string enrollCode, int employeeId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BiometricEmployeeReconciliationService.LockId})");
        var enrollment = await db.BiometricEnrollments.SingleOrDefaultAsync(x => x.EnrollCode == enrollCode);
        if (enrollment == null) return NotFound();
        if (await db.BiometricEmployeeExclusions.AnyAsync(x => x.DeviceUserCode == enrollCode))
            return Conflict("HR-deleted enrollments cannot be restored through automatic mapping.");
        var employee = employeeId == 0 ? null : await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId);
        if (employeeId != 0 && employee == null) return NotFound();
        if (employee == null)
        {
            if (string.IsNullOrWhiteSpace(enrollment.FullName)) return BadRequest("A source name is required before creating a profile.");
            string code;
            do
            {
                var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"EmployeeCodeSequence\"') AS \"Value\"").SingleAsync();
                code = $"Vertex-{number:D2}";
            } while (await db.Employees.AnyAsync(x => x.EmployeeCode == code));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enrollCode + Guid.NewGuid()))).ToLowerInvariant();
            var names = enrollment.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            employee = new Employee { EmployeeCode = code, FullName = enrollment.FullName,
                FirstName = names[0][..Math.Min(60, names[0].Length)], LastName = names.Length > 1 ? names[1][..Math.Min(60, names[1].Length)] : null,
                IsBiometricProfilePending = true, Email = "bio-" + hash[..32] + "@import.vertex", PhoneNumber = "BIO-" + hash[..16],
                Department = "Unassigned", Designation = "Employee", EmploymentType = "Full Time", EmployeeStatus = "Active", IsActive = true,
                JoiningDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330)) };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
        }
        var mappings = await db.EmployeeDeviceMappings.Where(x => x.DeviceUserId.ToUpper() == enrollCode).ToListAsync();
        var deviceIds = mappings.Select(x => x.BiometricDeviceId).ToList();
        if (await db.EmployeeDeviceMappings.AnyAsync(x => x.EmployeeId == employee.Id && deviceIds.Contains(x.BiometricDeviceId) && x.DeviceUserId.ToUpper() != enrollCode))
        {
            TempData["EnrollmentError"] = "This employee has a different enroll code on the same device. Resolve that link first.";
            return RedirectToAction(nameof(Index));
        }
        foreach (var mapping in mappings.Where(x => x.IsActive)) mapping.EmployeeId = employee.Id;
        enrollment.EmployeeId = employee.Id;
        enrollment.Status = "Linked"; enrollment.ReviewReason = null;
        db.AuditLogs.Add(AuditLogFactory.CreateEvent("BiometricEnrollment", "HR verified enrollment",
            $"Enroll code {enrollCode} linked to employee #{employee.Id}. Previously assigned attendance preserved.", User));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }
}
