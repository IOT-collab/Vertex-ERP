using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class EmployeeNumberingChecks
{
    public static async Task Run(DbContextOptions<ApplicationDbContext> options, Action<bool, string> check)
    {
        Employee NewEmployee(string code = "") => new()
        {
            EmployeeCode = code, CompanyCode = "VPC", FirstName = "Numbering", FullName = "Numbering Test",
            Email = Guid.NewGuid() + "@example.invalid", PhoneNumber = Guid.NewGuid().ToString("N")[..15],
            Department = "Test", Designation = "Tester"
        };
        await using var db = new ApplicationDbContext(options);
        var historical = NewEmployee("VPC0250");
        db.Employees.Add(historical); await db.SaveChangesAsync();
        var next = NewEmployee(); db.Employees.Add(next);
        await EmployeeNumberingService.SaveAsync(db, next, true);
        check(next.EmployeeCode == "VPC0251", "Numbering skips higher existing IDs");
        db.Employees.Remove(next); await db.SaveChangesAsync();
        var afterDelete = NewEmployee(); db.Employees.Add(afterDelete);
        await EmployeeNumberingService.SaveAsync(db, afterDelete, true);
        check(afterDelete.EmployeeCode == "VPC0252", "Deleted employee ID is never reused");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var concurrent = new ApplicationDbContext(options);
            var employee = NewEmployee(); concurrent.Employees.Add(employee);
            await EmployeeNumberingService.SaveAsync(concurrent, employee, true);
            return employee.EmployeeCode;
        }));
        check(results.Distinct().Count() == 6 && results.Order().SequenceEqual(Enumerable.Range(253, 6).Select(n => $"VPC{n:D4}")),
            "Six concurrent database sessions receive distinct consecutive IDs");
        db.ChangeTracker.Clear();
        check((await db.EmployeeCompanies.SingleAsync(x => x.Code == "VPC")).LastIssuedNumber == 258,
            "Concurrent saves persist the final company counter");
        // Exercise the actual upgrade against legacy employee rows in this isolated schema.
        db.ChangeTracker.Clear();
        var migration = new CompanyMigrationProbe();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.Operations(false), db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in generator.Generate(migration.Operations(true), db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        check(await db.Employees.Where(x => x.EmployeeCode.StartsWith("VAS")).AllAsync(x => x.CompanyCode == "VAS"),
            "Migration maps legacy VAS employees to Automations");
        check(await db.Employees.Where(x => x.EmployeeCode.StartsWith("VPC")).AllAsync(x => x.CompanyCode == "VPC"),
            "Migration maps legacy VPC employees to Power Controls");
        check((await db.EmployeeCompanies.SingleAsync(x => x.Code == "VPC")).LastIssuedNumber == 258,
            "Migration starts above existing higher IDs");
        check(await db.Employees.Where(x => !x.EmployeeCode.StartsWith("VAS") && !x.EmployeeCode.StartsWith("VPC")).AllAsync(x => x.CompanyCode == null),
            "Migration leaves unrecognized legacy IDs unassigned");
    }

    sealed class CompanyMigrationProbe : Vertex_ERP.Migrations.AddEmployeeCompanies
    {
        public IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> Operations(bool up)
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            if (up) base.Up(builder); else base.Down(builder);
            return builder.Operations;
        }
    }
}
