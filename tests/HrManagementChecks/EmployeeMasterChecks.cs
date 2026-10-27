using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

internal static class EmployeeMasterChecks
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var employee = new Employee { Id = 8, FullName = "HR Updated Name", Designation = "Updated Designation", Department = "Updated Department" };
        Check(EmployeeProfileDisplay.Name(employee, "Old WDMS Name") == employee.FullName, "Completed HR profile overrides stale WDMS name");
        employee.IsBiometricProfilePending = true;
        Check(EmployeeProfileDisplay.Name(employee, "WDMS Source Name") == "WDMS Source Name", "Pending imports retain WDMS names");
        employee.FullName = "Biometric User 08";
        Check(EmployeeProfileDisplay.Name(employee, null) == "Name not available in WDMS", "Biometric placeholder is never shown as an employee name");
        Check(EmployeeProfileDisplay.Name(null, "Unlinked Name") == "Unlinked Name", "Unlinked WDMS records retain source identity");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261027045143_AddEmployeeCategory", "20261027113000_SynchronizeEmployeeMasterLabels");
        Check(sql.Contains("employee.\"DepartmentId\" = department.\"Id\"") && sql.Contains("IS DISTINCT FROM"), "Existing department copies repair by stable ID");
        var userTable = db.Model.FindEntityType(typeof(AppUser))!.GetTableName();
        Check(userTable == "Users" && sql.Contains($"UPDATE \"{userTable}\" AS account")
            && !sql.Contains("UPDATE \"AppUsers\""), "Migration uses mapped Users table, not DbSet property name");
        Check(sql.Contains("account.\"EmployeeId\" = employee.\"Id\""), "Existing login names repair by employee ID");
        Check(!sql.Contains("UPDATE \"AttendanceLogs\"") && !sql.Contains("UPDATE \"Salary"), "Master repair preserves attendance evidence and financial history");
        Console.WriteLine("8 employee master checks passed.");
    }
}
