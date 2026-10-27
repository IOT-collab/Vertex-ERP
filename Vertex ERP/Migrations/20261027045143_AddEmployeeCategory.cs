using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployeeCategory",
                table: "Employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Employees_EmployeeCategory",
                table: "Employees",
                sql: "\"EmployeeCategory\" IS NULL OR \"EmployeeCategory\" IN ('Blue Collar', 'White Collar')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Employees_EmployeeCategory",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployeeCategory",
                table: "Employees");
        }
    }
}
