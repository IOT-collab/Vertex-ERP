using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddBiometricProfilePending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBiometricProfilePending",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            // Only untouched placeholders created by biometric reconciliation are pending.
            migrationBuilder.Sql("""
                UPDATE "Employees" e SET "IsBiometricProfilePending" = TRUE
                WHERE e."FirstName" = 'Biometric User'
                  AND e."Email" LIKE 'bio-%@import.vertex' AND e."PhoneNumber" LIKE 'BIO-%'
                  AND e."Department" = 'Unassigned' AND e."IsActive" = TRUE
                  AND EXISTS (SELECT 1 FROM "EmployeeDeviceMapping" m WHERE m."EmployeeId" = e."Id")
                  AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."EmployeeId" = e."Id")
                  AND NOT EXISTS (SELECT 1 FROM "EmployeeBankDetails" b WHERE b."EmployeeId" = e."Id")
                  AND NOT EXISTS (SELECT 1 FROM "EmployeeSalaryDetails" s WHERE s."EmployeeId" = e."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBiometricProfilePending",
                table: "Employees");
        }
    }
}
