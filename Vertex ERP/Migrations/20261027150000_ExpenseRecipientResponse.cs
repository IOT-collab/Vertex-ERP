using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseRecipientResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HrDecision",
                table: "ExpenseClaims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientDecision",
                table: "ExpenseClaims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientReason",
                table: "ExpenseClaims",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecipientRespondedAtUtc",
                table: "ExpenseClaims",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipientRespondedByUserId",
                table: "ExpenseClaims",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseContentType",
                table: "ExpenseClaims",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResponseFileSize",
                table: "ExpenseClaims",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseOriginalFileName",
                table: "ExpenseClaims",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseStoredFileName",
                table: "ExpenseClaims",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ExpenseClaims" SET "HrDecision" = "Status"
                WHERE "DecidedAtUtc" IS NOT NULL AND "HrDecision" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_RecipientRespondedByUserId",
                table: "ExpenseClaims",
                column: "RecipientRespondedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseClaims_Users_RecipientRespondedByUserId",
                table: "ExpenseClaims",
                column: "RecipientRespondedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseClaims_Users_RecipientRespondedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseClaims_RecipientRespondedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "HrDecision",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "RecipientDecision",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "RecipientReason",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "RecipientRespondedAtUtc",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "RecipientRespondedByUserId",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ResponseContentType",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ResponseFileSize",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ResponseOriginalFileName",
                table: "ExpenseClaims");

            migrationBuilder.DropColumn(
                name: "ResponseStoredFileName",
                table: "ExpenseClaims");
        }
    }
}
