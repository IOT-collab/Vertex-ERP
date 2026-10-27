using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyExtraLeaveFacilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExtraFacility",
                table: "LeaveRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ShortLeaveFrom",
                table: "LeaveRequests",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ShortLeaveTo",
                table: "LeaveRequests",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExtraFacility",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ShortLeaveFrom",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ShortLeaveTo",
                table: "LeaveRequests");
        }
    }
}
