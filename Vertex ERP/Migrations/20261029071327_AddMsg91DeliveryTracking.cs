using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddMsg91DeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                table: "WhatsAppTaskNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryErrorCode",
                table: "WhatsAppTaskNotifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "WhatsAppTaskNotifications",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryUpdatedAtUtc",
                table: "WhatsAppTaskNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaMessageId",
                table: "WhatsAppTaskNotifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "WhatsAppTaskNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientNumberHash",
                table: "WhatsAppTaskNotifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppDeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetaMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientNumberHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppDeliveryEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppDeliveryEvents_Applied_RequestId",
                table: "WhatsAppDeliveryEvents",
                columns: new[] { "Applied", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppDeliveryEvents_EventKey",
                table: "WhatsAppDeliveryEvents",
                column: "EventKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppDeliveryEvents");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "DeliveryErrorCode",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "DeliveryUpdatedAtUtc",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "MetaMessageId",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "WhatsAppTaskNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientNumberHash",
                table: "WhatsAppTaskNotifications");
        }
    }
}
