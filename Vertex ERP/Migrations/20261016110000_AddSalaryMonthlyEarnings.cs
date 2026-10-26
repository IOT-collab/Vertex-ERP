using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261016110000_AddSalaryMonthlyEarnings")]
public sealed class AddSalaryMonthlyEarnings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("MonthlyEarningsJson", "GeneratedSalarySlips", type: "text", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("MonthlyEarningsJson", "GeneratedSalarySlips");
}
