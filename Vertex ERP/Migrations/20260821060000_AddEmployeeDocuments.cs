using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VertexERP.Data;

#nullable disable

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260821060000_AddEmployeeDocuments")]
public partial class AddEmployeeDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmployeeDocuments",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EmployeeId = table.Column<int>(type: "integer", nullable: false),
                DocumentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                DocumentName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                StoredFileName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FileSize = table.Column<long>(type: "bigint", nullable: false),
                ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UploadedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                table.ForeignKey(name: "FK_EmployeeDocuments_Employees_EmployeeId", column: x => x.EmployeeId, principalTable: "Employees", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_EmployeeDocuments_EmployeeId_DocumentType", table: "EmployeeDocuments", columns: new[] { "EmployeeId", "DocumentType" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "EmployeeDocuments");
}
