using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Data;

#nullable disable

namespace Vertex_ERP.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261007000001_TrackFirebaseSmsSends")]
    public partial class TrackFirebaseSmsSends : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<DateTime>(
                name: "SmsSentAtUtc",
                table: "PasswordResetTokens",
                type: "timestamp with time zone",
                nullable: true);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropColumn(name: "SmsSentAtUtc", table: "PasswordResetTokens");
    }
}
