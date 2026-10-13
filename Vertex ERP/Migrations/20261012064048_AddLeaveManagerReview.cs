using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveManagerReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerDecidedAtUtc",
                table: "LeaveRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecidedByName",
                table: "LeaveRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerDecidedByUserId",
                table: "LeaveRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecision",
                table: "LeaveRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecisionNote",
                table: "LeaveRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerDecidedAtUtc",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedByName",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedByUserId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ManagerDecision",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "ManagerDecisionNote",
                table: "LeaveRequests");
        }
    }
}
