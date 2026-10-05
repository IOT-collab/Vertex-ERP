using Microsoft.EntityFrameworkCore;
using Npgsql;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class EmployeeNumberingService
{
    public static async Task<string> PreviewAsync(ApplicationDbContext db, string companyCode)
    {
        var company = await db.EmployeeCompanies.AsNoTracking().SingleAsync(x => x.Code == companyCode);
        var codes = await db.Employees.AsNoTracking().Where(x => x.EmployeeCode.ToUpper().StartsWith(companyCode))
            .Select(x => x.EmployeeCode).ToListAsync();
        var maximum = codes.Select(code => int.TryParse(code.AsSpan(3), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return companyCode + checked(Math.Max(company.LastIssuedNumber, maximum) + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Counter and employee are committed in the same SaveChanges transaction.
    // Optimistic concurrency also protects separate app instances.
    public static async Task SaveAsync(ApplicationDbContext db, Employee employee, bool allocate)
    {
        if (!allocate) { await db.SaveChangesAsync(); return; }
        var company = await db.EmployeeCompanies.SingleAsync(x => x.Code == employee.CompanyCode);
        for (var attempt = 0; ; attempt++)
        {
            var codes = await db.Employees.AsNoTracking()
                .Where(x => x.EmployeeCode.ToUpper().StartsWith(company.Code))
                .Select(x => x.EmployeeCode).ToListAsync();
            var maximum = codes.Select(code => int.TryParse(code.AsSpan(3), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            company.LastIssuedNumber = checked(Math.Max(company.LastIssuedNumber, maximum) + 1);
            employee.EmployeeCode = company.Code + company.LastIssuedNumber.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            try { await db.SaveChangesAsync(); return; }
            catch (DbUpdateConcurrencyException ex) when (attempt < 9 && ex.Entries.All(x => x.Entity is EmployeeCompany))
            {
                await db.Entry(company).ReloadAsync();
            }
            catch (DbUpdateException ex) when (attempt < 9 && ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Employees_EmployeeCode" })
            {
                await db.Entry(company).ReloadAsync();
            }
        }
    }
}
