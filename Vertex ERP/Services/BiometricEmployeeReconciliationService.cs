using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

/// <summary>Turns accepted device punches into ordinary, editable ERP employees.</summary>
public sealed class BiometricEmployeeReconciliationService(IServiceScopeFactory scopes,
    ILogger<BiometricEmployeeReconciliationService> logger) : BackgroundService
{
    // Shared by ERP/receiver instances and employee deletion.
    public const long LockId = 714920176;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await ReconcileAsync(db, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Biometric employee reconciliation failed; will retry."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public static async Task<int> ReconcileAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockId})", cancellationToken);
            var logs = await db.AttendanceLogs.Where(x => x.EmployeeId == null && x.BiometricDevice.IsActive && x.DeviceUserId.Trim() != ""
                && !db.EmployeeDeviceMappings.Any(m => m.BiometricDeviceId == x.BiometricDeviceId && !m.IsActive && m.DeviceUserId.ToUpper() == x.DeviceUserId.Trim().ToUpper())
                && !db.BiometricEmployeeExclusions.Any(e => e.DeviceUserCode == x.DeviceUserId.Trim().ToUpper()))
                .OrderBy(x => x.Id).Take(5000).ToListAsync(cancellationToken);
            var count = 0;
            foreach (var group in logs.GroupBy(x => (x.BiometricDeviceId, Code: x.DeviceUserId.Trim().ToUpperInvariant())))
            {
                if (group.Key.Code.Length == 0) continue;
                var mapping = await db.EmployeeDeviceMappings.Include(x => x.Employee)
                    .FirstOrDefaultAsync(x => x.BiometricDeviceId == group.Key.BiometricDeviceId && x.DeviceUserId.ToUpper() == group.Key.Code, cancellationToken);
                // An explicitly disabled mapping must not be re-enabled by import.
                if (mapping is { IsActive: false }) continue;
                var employee = mapping?.Employee;
                if (employee == null)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key.Code))).ToLowerInvariant();
                    var canonicalCode = group.Key.Code.Length <= 30 ? group.Key.Code : "BIO-" + hash[..26];
                    // Match existing HR/imported codes before creating a profile. Never change an existing profile.
                    // Resolve the source identity before the editable ERP employee code.
                    var linkedIds = await db.EmployeeDeviceMappings
                        .Where(x => x.IsActive && x.DeviceUserId.ToUpper() == group.Key.Code)
                        .Select(x => x.EmployeeId).Distinct().Take(2).ToListAsync(cancellationToken);
                    if (linkedIds.Count > 1) continue;
                    employee = linkedIds.Count == 1
                        ? await db.Employees.SingleAsync(x => x.Id == linkedIds[0], cancellationToken)
                        : await db.Employees.FirstOrDefaultAsync(x =>
                            (x.EmployeeCode.ToUpper() == canonicalCode.ToUpper() || x.EmployeeCode.ToUpper() == "BIO-" + group.Key.Code)
                            && !db.EmployeeDeviceMappings.Any(m => m.EmployeeId == x.Id), cancellationToken);
                    if (employee == null)
                    {
                        string erpCode;
                        do
                        {
                            var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"EmployeeCodeSequence\"') AS \"Value\"")
                                .SingleAsync(cancellationToken);
                            erpCode = $"Vertex-{number:D2}";
                        } while (await db.Employees.AnyAsync(x => x.EmployeeCode.ToUpper() == erpCode.ToUpper(), cancellationToken));
                        employee = new Employee
                        {
                            EmployeeCode = erpCode,
                            IsBiometricProfilePending = true,
                            FirstName = "Biometric User", LastName = group.Key.Code,
                            FullName = "Biometric User " + group.Key.Code,
                            // Explicit placeholders, never guessed personal/contact details or login credentials.
                            Email = "bio-" + hash[..32] + "@import.vertex", PhoneNumber = "BIO-" + hash[..16],
                            Department = "Unassigned", Designation = "Employee",
                            JoiningDate = DateOnly.FromDateTime(group.Min(x => x.PunchTime)),
                            EmploymentType = "Full Time", EmployeeStatus = "Active", IsActive = true
                        };
                        db.Employees.Add(employee);
                        count++;
                    }
                    mapping = new EmployeeDeviceMapping { BiometricDeviceId = group.Key.BiometricDeviceId, DeviceUserId = group.First().DeviceUserId, Employee = employee, IsActive = true };
                    db.EmployeeDeviceMappings.Add(mapping);
                }
                foreach (var log in group) log.Employee = employee;
                // Save each identity so following devices can resolve the same employee.
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return count;
        });
    }
}
