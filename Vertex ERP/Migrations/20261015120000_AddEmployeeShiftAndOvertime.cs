using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015120000_AddEmployeeShiftAndOvertime")]
public sealed class AddEmployeeShiftAndOvertime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("OvertimeEnabled", "Employees", type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<TimeOnly>("ShiftStartTime", "Employees", type: "time without time zone", nullable: false, defaultValue: new TimeOnly(9, 30));
        migrationBuilder.AddColumn<TimeOnly>("ShiftEndTime", "Employees", type: "time without time zone", nullable: false, defaultValue: new TimeOnly(18, 0));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("OvertimeEnabled", "Employees");
        migrationBuilder.DropColumn("ShiftStartTime", "Employees");
        migrationBuilder.DropColumn("ShiftEndTime", "Employees");
    }
}
