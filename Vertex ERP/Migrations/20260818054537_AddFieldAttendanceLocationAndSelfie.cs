using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldAttendanceLocationAndSelfie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AccuracyMetres",
                table: "AttendanceLogs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "AttendanceLogs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "AttendanceLogs",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfiePath",
                table: "AttendanceLogs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccuracyMetres",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "SelfiePath",
                table: "AttendanceLogs");
        }
    }
}
