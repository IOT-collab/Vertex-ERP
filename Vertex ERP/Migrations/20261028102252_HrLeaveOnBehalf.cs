using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class HrLeaveOnBehalf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmittedByHrName",
                table: "LeaveRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedByHrUserId",
                table: "LeaveRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_SubmittedByHrUserId",
                table: "LeaveRequests",
                column: "SubmittedByHrUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Users_SubmittedByHrUserId",
                table: "LeaveRequests",
                column: "SubmittedByHrUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Users_SubmittedByHrUserId",
                table: "LeaveRequests");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequests_SubmittedByHrUserId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedByHrName",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedByHrUserId",
                table: "LeaveRequests");
        }
    }
}
