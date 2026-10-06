using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetEmployeeAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeAssets_AssetTag",
                table: "EmployeeAssets");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "EmployeeAssets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAtUtc",
                table: "EmployeeAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "EmployeeAssets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Issued");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssets_AssetTag",
                table: "EmployeeAssets",
                column: "AssetTag",
                unique: true,
                filter: "\"Status\" <> 'Declined'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeAssets_AssetTag",
                table: "EmployeeAssets");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "EmployeeAssets");

            migrationBuilder.DropColumn(
                name: "RespondedAtUtc",
                table: "EmployeeAssets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EmployeeAssets");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssets_AssetTag",
                table: "EmployeeAssets",
                column: "AssetTag",
                unique: true);
        }
    }
}
