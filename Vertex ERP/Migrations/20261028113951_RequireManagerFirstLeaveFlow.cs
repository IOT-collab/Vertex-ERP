using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class RequireManagerFirstLeaveFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LeaveRequests" AS r
                SET "ApprovalLevel" = 'Manager', "AssignedApproverEmployeeId" = e."ReportingManagerId"
                FROM "Employees" AS e
                JOIN "Employees" AS m ON m."Id" = e."ReportingManagerId"
                WHERE r."EmployeeId" = e."Id" AND e."IsActive" AND m."IsActive" AND e."ReportingManagerId" <> e."Id"
                  AND r."Status" = 'Pending' AND r."ManagerDecision" IS NULL AND r."ManagerDecidedAtUtc" IS NULL
                  AND r."ApprovalLevel" IN ('Manager', 'HR/Admin')
                  AND (r."ApprovalLevel" <> 'Manager' OR r."AssignedApproverEmployeeId" IS DISTINCT FROM e."ReportingManagerId")
                  AND EXISTS (SELECT 1 FROM "Users" AS u WHERE u."EmployeeId" = m."Id" AND u."IsActive"
                              AND lower(u."Role") IN ('manager', 'supervisor', 'employee', 'user'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not reverse routing once a subsequent manager/HR decision may have been recorded.
        }
    }
}
