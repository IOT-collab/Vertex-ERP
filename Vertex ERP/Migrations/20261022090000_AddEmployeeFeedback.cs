using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VertexERP.Data;

namespace Vertex_ERP.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022090000_AddEmployeeFeedback")]
public sealed class AddEmployeeFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("EmployeeFeedbacks", table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            SubmittedByUserId = table.Column<int>(type: "integer", nullable: true),
            SenderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            SenderRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
            EmployeeCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
            Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            Subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
            Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
            SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_EmployeeFeedbacks", x => x.Id);
            table.ForeignKey("FK_EmployeeFeedbacks_Users_SubmittedByUserId", x => x.SubmittedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
        });
        migrationBuilder.CreateIndex("IX_EmployeeFeedbacks_SubmittedAtUtc", "EmployeeFeedbacks", "SubmittedAtUtc");
        migrationBuilder.CreateIndex("IX_EmployeeFeedbacks_SubmittedByUserId", "EmployeeFeedbacks", "SubmittedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("EmployeeFeedbacks");
}
