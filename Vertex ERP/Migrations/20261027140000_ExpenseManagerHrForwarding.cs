using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseManagerHrForwarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ForwardedAtUtc",
                table: "ExpenseClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ForwardedByUserId",
                table: "ExpenseClaims",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ForwardedToEmployeeId",
                table: "ExpenseClaims",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerDecidedAtUtc",
                table: "ExpenseClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerDecidedByUserId",
                table: "ExpenseClaims",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecision",
                table: "ExpenseClaims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerNote",
                table: "ExpenseClaims",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowStage",
                table: "ExpenseClaims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manager");

            migrationBuilder.AddColumn<bool>(
                name: "CanReceiveExpenseClaims",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "ExpenseClaims" SET "WorkflowStage" = CASE
                    WHEN "Status" <> 'Pending' THEN 'Reviewed'
                    WHEN "RequiresHrApproval" THEN 'HR' ELSE 'Manager' END;
                UPDATE "ExpenseClaims" SET "ManagerDecision" = "Status", "ManagerNote" = "DecisionNote",
                    "ManagerDecidedAtUtc" = "DecidedAtUtc", "ManagerDecidedByUserId" = "DecidedByUserId"
                WHERE NOT "RequiresHrApproval" AND "Status" <> 'Pending';
                WITH candidates AS (
                    SELECT DISTINCT employee."Id" FROM "Employees" employee
                    JOIN "Users" account ON account."EmployeeId" = employee."Id" AND account."IsActive"
                    WHERE employee."IsActive" AND (lower(trim(employee."FullName")) = 'yuvraj singh'
                        OR lower(trim(account."FullName")) = 'yuvraj singh'
                        OR EXISTS (SELECT 1 FROM "BiometricEnrollments" enrollment
                            WHERE enrollment."EmployeeId" = employee."Id" AND enrollment."Status" = 'Linked'
                              AND lower(trim(enrollment."WdmsName")) = 'yuvraj singh'))
                ), chosen AS (SELECT min("Id") AS "Id" FROM candidates HAVING count(*) = 1)
                UPDATE "Employees" employee SET "CanReceiveExpenseClaims" = TRUE
                FROM chosen WHERE chosen."Id" = employee."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_ForwardedByUserId",
                table: "ExpenseClaims",
                column: "ForwardedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_ForwardedToEmployeeId",
                table: "ExpenseClaims",
                column: "ForwardedToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_ManagerDecidedByUserId",
                table: "ExpenseClaims",
                column: "ManagerDecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_WorkflowStage_ForwardedToEmployeeId",
                table: "ExpenseClaims",
                columns: new[] { "WorkflowStage", "ForwardedToEmployeeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseClaims_Employees_ForwardedToEmployeeId",
                table: "ExpenseClaims",
                column: "ForwardedToEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseClaims_Users_ForwardedByUserId",
                table: "ExpenseClaims",
                column: "ForwardedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseClaims_Users_ManagerDecidedByUserId",
                table: "ExpenseClaims",
                column: "ManagerDecidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseClaims_Employees_ForwardedToEmployeeId",
                table: "ExpenseClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseClaims_Users_ForwardedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseClaims_Users_ManagerDecidedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaims_ForwardedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaims_ForwardedToEmployeeId",
                table: "ExpenseClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaims_ManagerDecidedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaims_WorkflowStage_ForwardedToEmployeeId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ForwardedAtUtc",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ForwardedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ForwardedToEmployeeId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedAtUtc",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ManagerDecision",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ManagerNote",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "WorkflowStage",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "CanReceiveExpenseClaims",
                table: "Employees");
        }
    }
}
