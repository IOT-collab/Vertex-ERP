using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public sealed record EnrollmentIdentity(string EnrollCode, string FullName);

public static class BiometricEnrollmentService
{
    public static string NormalizeName(string name) => string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static async Task SyncAsync(ApplicationDbContext db, IReadOnlyList<EnrollmentIdentity> identities, CancellationToken ct = default)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BiometricEmployeeReconciliationService.LockId})", ct);
            var registry = await db.BiometricEnrollments.Include(x => x.Employee).ToDictionaryAsync(x => x.EnrollCode, ct);
            foreach (var group in identities.Where(x => !string.IsNullOrWhiteSpace(x.EnrollCode)).GroupBy(x => x.EnrollCode.Trim().ToUpperInvariant()))
            {
                if (group.Key.Length is 0 or > 50) continue;
                var names = group.Select(x => x.FullName.Trim()).Where(x => x.Length is > 0 and <= 120).DistinctBy(NormalizeName).ToList();
                if (!registry.TryGetValue(group.Key, out var enrollment))
                {
                    enrollment = new BiometricEnrollment { EnrollCode = group.Key };
                    db.BiometricEnrollments.Add(enrollment);
                    registry.Add(group.Key, enrollment);
                }
                enrollment.LastSeenUtc = DateTime.UtcNow;
                if (names.Count != 1)
                {
                    enrollment.Status = "Needs review";
                    enrollment.ReviewReason = "WDMS supplied a missing or conflicting name for this enroll code.";
                    continue;
                }
                enrollment.WdmsName = names[0];
                if (!enrollment.IsSheetReference) enrollment.FullName = names[0];
            }
            var employees = await db.Employees.ToListAsync(ct);
            var mappings = await db.EmployeeDeviceMappings.ToListAsync(ct);
            var excluded = (await db.BiometricEmployeeExclusions.Select(x => x.DeviceUserCode).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var enrollment in registry.Values)
            {
                if (excluded.Contains(enrollment.EnrollCode))
                {
                    enrollment.EmployeeId = null; enrollment.Employee = null;
                    enrollment.Status = "Excluded"; enrollment.ReviewReason = "This enrollment was deleted by HR.";
                    continue;
                }
                if (enrollment.Status == "Needs review") continue;
                if (enrollment.EmployeeId.HasValue)
                {
                    // A verified link survives subsequent HR name/ERP-code edits.
                    enrollment.Status = "Linked";
                    if (enrollment.Employee is { IsBiometricProfilePending: true } pending)
                    {
                        var parts = enrollment.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        pending.FullName = enrollment.FullName;
                        pending.FirstName = parts[0][..Math.Min(60, parts[0].Length)];
                        pending.LastName = parts.Length > 1 ? parts[1][..Math.Min(60, parts[1].Length)] : null;
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(enrollment.FullName)) continue;
                var linkedIds = mappings.Where(x => x.IsActive && x.DeviceUserId.Trim().Equals(enrollment.EnrollCode, StringComparison.OrdinalIgnoreCase)).Select(x => x.EmployeeId).Distinct().ToList();
                var sameName = employees.Where(x => NormalizeName(x.FullName) == NormalizeName(enrollment.FullName)).ToList();
                Employee? employee = null;
                if (linkedIds.Count > 1)
                    enrollment.ReviewReason = "This enroll code is linked to multiple ERP profiles.";
                else if (linkedIds.Count == 1)
                {
                    employee = employees.Single(x => x.Id == linkedIds[0]);
                    if (sameName.Any(x => x.Id != employee.Id) || (!employee.IsBiometricProfilePending && NormalizeName(employee.FullName) != NormalizeName(enrollment.FullName)))
                    {
                        enrollment.ReviewReason = $"Existing link: {employee.EmployeeCode} / {employee.FullName}. Verify against the source name.";
                        employee = null;
                    }
                }
                else if (sameName.Count == 1 && !mappings.Any(x => x.EmployeeId == sameName[0].Id)
                    && !registry.Values.Any(x => x.EmployeeId == sameName[0].Id)) employee = sameName[0];
                else if (sameName.Count > 0) enrollment.ReviewReason = "An employee with this name already exists. Verify their enroll code before linking.";
                else
                {
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enrollment.EnrollCode))).ToLowerInvariant();
                    string code;
                    do
                    {
                        var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"EmployeeCodeSequence\"') AS \"Value\"").SingleAsync(ct);
                        code = $"Vertex-{number:D2}";
                    } while (employees.Any(x => x.EmployeeCode.Equals(code, StringComparison.OrdinalIgnoreCase)));
                    employee = new Employee
                    {
                        EmployeeCode = code, IsBiometricProfilePending = true,
                        Email = "bio-" + hash[..32] + "@import.vertex", PhoneNumber = "BIO-" + hash[..16],
                        Department = "Unassigned", Designation = "Employee", EmploymentType = "Full Time",
                        EmployeeStatus = "Active", IsActive = true, JoiningDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330))
                    };
                    db.Employees.Add(employee); employees.Add(employee);
                }
                if (employee == null) { enrollment.Status = "Needs review"; continue; }
                if (employee.IsBiometricProfilePending)
                {
                    var parts = enrollment.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    employee.FirstName = parts[0][..Math.Min(60, parts[0].Length)];
                    employee.LastName = parts.Length > 1 ? parts[1][..Math.Min(60, parts[1].Length)] : null;
                    employee.FullName = enrollment.FullName;
                }
                enrollment.Employee = employee;
                enrollment.Status = "Linked"; enrollment.ReviewReason = null;
                await db.SaveChangesAsync(ct);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
