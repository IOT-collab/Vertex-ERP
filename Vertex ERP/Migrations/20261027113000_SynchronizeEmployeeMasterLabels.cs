using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027113000_SynchronizeEmployeeMasterLabels")]
public sealed class SynchronizeEmployeeMasterLabels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE "Employees" AS employee
        SET "Department" = department."DepartmentName"
        FROM "Departments" AS department
        WHERE employee."DepartmentId" = department."Id"
          AND employee."Department" IS DISTINCT FROM department."DepartmentName";

        UPDATE "Users" AS account
        SET "FullName" = employee."FullName"
        FROM "Employees" AS employee
        WHERE account."EmployeeId" = employee."Id"
          AND length(employee."FullName") <= 100
          AND account."FullName" IS DISTINCT FROM employee."FullName";
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Old labels are stale copies, not a previous version of master data.
    }
}
