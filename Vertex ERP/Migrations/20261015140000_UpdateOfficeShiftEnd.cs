using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015140000_UpdateOfficeShiftEnd")]
public sealed class UpdateOfficeShiftEnd : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Employees" SET "ShiftEndTime" = TIME '18:30:00'
            WHERE "ShiftStartTime" = TIME '09:30:00' AND "ShiftEndTime" = TIME '18:00:00';
            ALTER TABLE "Employees" ALTER COLUMN "ShiftEndTime" SET DEFAULT '18:30:00';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not overwrite individually assigned shifts when rolling back the default.
        migrationBuilder.Sql("ALTER TABLE \"Employees\" ALTER COLUMN \"ShiftEndTime\" SET DEFAULT '18:00:00';");
    }
}
