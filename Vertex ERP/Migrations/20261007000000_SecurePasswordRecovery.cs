using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

#nullable disable

namespace Vertex_ERP.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261007000000_SecurePasswordRecovery")]
    public partial class SecurePasswordRecovery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "PasswordResetTokens",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "PasswordResetTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VerificationMethod", table: "PasswordResetTokens");
            migrationBuilder.DropColumn(name: "VerifiedAtUtc", table: "PasswordResetTokens");
            migrationBuilder.DropColumn(name: "PasswordChangedAtUtc", table: "Users");
        }
    }
}
