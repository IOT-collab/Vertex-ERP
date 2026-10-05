using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyCode",
                table: "Employees",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeCompanies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LastIssuedNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCompanies", x => x.Code);
                });

            migrationBuilder.InsertData(
                table: "EmployeeCompanies",
                columns: new[] { "Code", "LastIssuedNumber", "Name" },
                values: new object[,]
                {
                    { "VAS", 179, "Vertex Automations Pvt Ltd" },
                    { "VPC", 177, "Vertex Power Controls Pvt. Ltd" }
                });

            // Classify only recognized numeric employee IDs; preserve all existing IDs.
            migrationBuilder.Sql("""
                UPDATE "Employees" SET "CompanyCode" = LEFT(UPPER("EmployeeCode"), 3)
                WHERE UPPER("EmployeeCode") ~ '^(VAS|VPC)[0-9]+$';
                UPDATE "EmployeeCompanies" c SET "LastIssuedNumber" = GREATEST(c."LastIssuedNumber",
                    COALESCE((SELECT MAX(SUBSTRING(e."EmployeeCode" FROM 4)::numeric)
                        FROM "Employees" e WHERE e."CompanyCode" = c."Code"), 0))::integer;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyCode",
                table: "Employees",
                column: "CompanyCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_EmployeeCompanies_CompanyCode",
                table: "Employees",
                column: "CompanyCode",
                principalTable: "EmployeeCompanies",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_EmployeeCompanies_CompanyCode",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "EmployeeCompanies");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CompanyCode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CompanyCode",
                table: "Employees");
        }
    }
}
