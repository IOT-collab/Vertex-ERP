using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class HardenAttendanceDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AttendanceLogs" duplicate
                USING "AttendanceLogs" original
                WHERE duplicate."Id" > original."Id"
                  AND duplicate."BiometricDeviceId" = original."BiometricDeviceId"
                  AND duplicate."DeviceUserId" = original."DeviceUserId"
                  AND duplicate."PunchTime" = original."PunchTime";
                """);

            migrationBuilder.DropIndex(
                name: "IX_AttendanceLogs_BiometricDeviceId_DeviceUserId_PunchTime",
                table: "AttendanceLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_BiometricDeviceId_DeviceUserId_PunchTime",
                table: "AttendanceLogs",
                columns: new[] { "BiometricDeviceId", "DeviceUserId", "PunchTime" },
                unique: true);

            migrationBuilder.Sql("""
                DELETE FROM "AttendanceLogs" duplicate
                USING "AttendanceLogs" original
                WHERE duplicate."Id" > original."Id"
                  AND duplicate."EmployeeId" = original."EmployeeId"
                  AND CAST(duplicate."PunchTime" AS date) = CAST(original."PunchTime" AS date)
                  AND duplicate."PunchState" = original."PunchState"
                  AND duplicate."WorkCode" = 'Field Attendance'
                  AND original."WorkCode" = 'Field Attendance';

                CREATE UNIQUE INDEX "IX_AttendanceLogs_FieldEmployeeDateAction"
                ON "AttendanceLogs" ("EmployeeId", (CAST("PunchTime" AS date)), "PunchState")
                WHERE "WorkCode" = 'Field Attendance' AND "EmployeeId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AttendanceLogs_FieldEmployeeDateAction\";");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceLogs_BiometricDeviceId_DeviceUserId_PunchTime",
                table: "AttendanceLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_BiometricDeviceId_DeviceUserId_PunchTime",
                table: "AttendanceLogs",
                columns: new[] { "BiometricDeviceId", "DeviceUserId", "PunchTime" });
        }
    }
}
