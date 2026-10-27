using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class RestrictManualAttendanceOperators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanManageManualAttendance",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            // Resolve each named operator once to a unique active employee/login.
            // Authorization subsequently uses the persisted employee ID, never a display name.
            migrationBuilder.Sql("""
                WITH intended(name) AS (VALUES ('yuvraj singh'), ('deepa katariya')),
                candidates AS (
                    SELECT DISTINCT intended.name, employee."Id"
                    FROM intended
                    JOIN "Employees" employee ON employee."IsActive"
                    JOIN "Users" account ON account."EmployeeId" = employee."Id" AND account."IsActive"
                    WHERE lower(trim(account."Role")) <> 'hr'
                      AND (lower(trim(employee."FullName")) = intended.name
                        OR lower(trim(account."FullName")) = intended.name
                        OR EXISTS (
                            SELECT 1 FROM "BiometricEnrollments" enrollment
                            WHERE enrollment."EmployeeId" = employee."Id" AND enrollment."Status" = 'Linked'
                              AND lower(trim(enrollment."WdmsName")) = intended.name))
                ), unique_operators AS (
                    SELECT min("Id") AS "Id" FROM candidates GROUP BY name HAVING count(*) = 1
                )
                UPDATE "Employees" employee SET "CanManageManualAttendance" = TRUE
                FROM unique_operators granted WHERE employee."Id" = granted."Id";
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanManageManualAttendance",
                table: "Employees");
        }
    }
}
