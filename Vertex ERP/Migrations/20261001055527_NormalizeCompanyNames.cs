using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCompanyNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "EmployeeCompanies",
                keyColumn: "Code",
                keyValue: "VAS",
                column: "Name",
                value: "Vertex Automation System Pvt. Ltd.");

            migrationBuilder.UpdateData(
                table: "EmployeeCompanies",
                keyColumn: "Code",
                keyValue: "VPC",
                column: "Name",
                value: "Vertex Power Controls Pvt. Ltd.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "EmployeeCompanies",
                keyColumn: "Code",
                keyValue: "VAS",
                column: "Name",
                value: "Vertex Automations Pvt Ltd");

            migrationBuilder.UpdateData(
                table: "EmployeeCompanies",
                keyColumn: "Code",
                keyValue: "VPC",
                column: "Name",
                value: "Vertex Power Controls Pvt. Ltd");
        }
    }
}
