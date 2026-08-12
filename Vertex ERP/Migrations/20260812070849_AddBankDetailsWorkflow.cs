using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddBankDetailsWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankDetailUpdateRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProtectedAccountNumber = table.Column<string>(type: "text", nullable: false),
                    AccountLastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    IfscCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AccountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PanNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UanNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EsicNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UpiId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HrNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankDetailUpdateRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankDetailUpdateRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeBankDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProtectedAccountNumber = table.Column<string>(type: "text", nullable: false),
                    AccountLastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    IfscCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AccountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PanNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UanNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EsicNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UpiId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeBankDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeBankDetails_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankDetailUpdateRequests_EmployeeId_Status",
                table: "BankDetailUpdateRequests",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankDetails_EmployeeId",
                table: "EmployeeBankDetails",
                column: "EmployeeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankDetailUpdateRequests");

            migrationBuilder.DropTable(
                name: "EmployeeBankDetails");
        }
    }
}
