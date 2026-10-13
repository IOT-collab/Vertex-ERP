using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddYearlyLeaveCalculator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LegacyUsedDays",
                table: "ManualLeaveBalances",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHalfDay",
                table: "LeaveRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LeaveCalculations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    MonthsJson = table.Column<string>(type: "text", nullable: false),
                    OpeningEL = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    OpeningEW = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveCalculations_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveCalculations_EmployeeId_Year",
                table: "LeaveCalculations",
                columns: new[] { "EmployeeId", "Year" },
                unique: true);

            // Existing UsedDays was a total entered by HR. Preserve that input and
            // treat already-approved requests as included, avoiding double usage.
            // The UI exposes the original total for HR reconciliation.
            migrationBuilder.Sql("""
                UPDATE "ManualLeaveBalances" b
                SET "LegacyUsedDays" = b."UsedDays",
                    "UsedDays" = GREATEST(0, b."UsedDays" - (
                        SELECT COUNT(DISTINCT d.day)::numeric
                        FROM "LeaveRequests" r
                        CROSS JOIN LATERAL generate_series(
                            GREATEST(r."FromDate", make_date(b."Year", 1, 1))::timestamp,
                            LEAST(r."ToDate", make_date(b."Year", 12, 31))::timestamp,
                            interval '1 day') AS d(day)
                        WHERE r."EmployeeId" = b."EmployeeId" AND r."Status" = 'Approved'
                        AND (CASE UPPER(TRIM(r."LeaveType"))
                            WHEN 'EARNED LEAVE' THEN 'EL'
                            WHEN 'CASUAL LEAVE' THEN 'CL'
                            ELSE UPPER(TRIM(r."LeaveType")) END) = b."Category"
                    )),
                    "Revision" = b."Revision" + 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ManualLeaveBalances" b SET "UsedDays" = b."UsedDays" + (
                    SELECT COALESCE(SUM(days.weight), 0) FROM (
                        SELECT d.day, MAX(CASE WHEN r."IsHalfDay" THEN 0.5 ELSE 1 END) AS weight
                        FROM "LeaveRequests" r
                        CROSS JOIN LATERAL generate_series(
                            GREATEST(r."FromDate", make_date(b."Year", 1, 1))::timestamp,
                            LEAST(r."ToDate", make_date(b."Year", 12, 31))::timestamp,
                            interval '1 day') AS d(day)
                        WHERE r."EmployeeId" = b."EmployeeId" AND r."Status" = 'Approved'
                        AND (CASE UPPER(TRIM(r."LeaveType")) WHEN 'EARNED LEAVE' THEN 'EL'
                            WHEN 'CASUAL LEAVE' THEN 'CL' ELSE UPPER(TRIM(r."LeaveType")) END) = b."Category"
                        GROUP BY d.day
                    ) days
                );
                """);
            migrationBuilder.DropTable(
                name: "LeaveCalculations");

            migrationBuilder.DropColumn(
                name: "LegacyUsedDays",
                table: "ManualLeaveBalances");

            migrationBuilder.DropColumn(
                name: "IsHalfDay",
                table: "LeaveRequests");
        }
    }
}
