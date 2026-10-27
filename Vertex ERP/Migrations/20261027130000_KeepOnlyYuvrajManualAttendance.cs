using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027130000_KeepOnlyYuvrajManualAttendance")]
public sealed class KeepOnlyYuvrajManualAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        WITH candidates AS (
            SELECT DISTINCT employee."Id"
            FROM "Employees" employee
            JOIN "Users" account ON account."EmployeeId" = employee."Id" AND account."IsActive"
            WHERE employee."IsActive" AND lower(trim(account."Role")) <> 'hr'
              AND (lower(trim(employee."FullName")) = 'yuvraj singh'
                OR lower(trim(account."FullName")) = 'yuvraj singh'
                OR EXISTS (SELECT 1 FROM "BiometricEnrollments" enrollment
                    WHERE enrollment."EmployeeId" = employee."Id" AND enrollment."Status" = 'Linked'
                      AND lower(trim(enrollment."WdmsName")) = 'yuvraj singh'))
        ), chosen AS (SELECT min("Id") AS "Id" FROM candidates HAVING count(*) = 1)
        UPDATE "Employees" employee
        SET "CanManageManualAttendance" = EXISTS (SELECT 1 FROM chosen WHERE chosen."Id" = employee."Id")
        WHERE employee."CanManageManualAttendance" OR EXISTS (SELECT 1 FROM chosen WHERE chosen."Id" = employee."Id");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
