using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015130000_RequireOvertimeOptIn")]
public sealed class RequireOvertimeOptIn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The previous rollout enabled all employees automatically. Start the
        // opt-in policy once with everyone inactive; subsequent HR saves persist.
        migrationBuilder.Sql("UPDATE \"Employees\" SET \"OvertimeEnabled\" = FALSE;");
        migrationBuilder.Sql("ALTER TABLE \"Employees\" ALTER COLUMN \"OvertimeEnabled\" SET DEFAULT FALSE;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve individually saved settings when reverting the default.
        migrationBuilder.Sql("ALTER TABLE \"Employees\" ALTER COLUMN \"OvertimeEnabled\" SET DEFAULT TRUE;");
    }
}
