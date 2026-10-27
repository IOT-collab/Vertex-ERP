using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026100000_SetOfficeStart0945")]
public sealed class SetOfficeStart0945 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "Employees" ALTER COLUMN "ShiftStartTime" SET DEFAULT '09:45:00';
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve individually assigned employee shifts on rollback.
        migrationBuilder.Sql("ALTER TABLE \"Employees\" ALTER COLUMN \"ShiftStartTime\" SET DEFAULT '09:30:00';");
    }
}
