using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddBiometricIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BiometricDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BranchCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServerAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ServerPort = table.Column<int>(type: "integer", nullable: false),
                    CommunicationMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirmwareVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastKnownIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiometricDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BiometricDeviceId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    DeviceUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PunchTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PunchState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    VerificationMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    WorkCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UniqueHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    SourceIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceLogs_BiometricDevices_BiometricDeviceId",
                        column: x => x.BiometricDeviceId,
                        principalTable: "BiometricDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceLogs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDeviceMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BiometricDeviceId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    DeviceUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDeviceMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDeviceMapping_BiometricDevices_BiometricDeviceId",
                        column: x => x.BiometricDeviceId,
                        principalTable: "BiometricDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeDeviceMapping_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_BiometricDeviceId_DeviceUserId_PunchTime",
                table: "AttendanceLogs",
                columns: new[] { "BiometricDeviceId", "DeviceUserId", "PunchTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_EmployeeId_PunchTime",
                table: "AttendanceLogs",
                columns: new[] { "EmployeeId", "PunchTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_UniqueHash",
                table: "AttendanceLogs",
                column: "UniqueHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BiometricDevices_SerialNumber",
                table: "BiometricDevices",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeviceMapping_BiometricDeviceId_DeviceUserId",
                table: "EmployeeDeviceMapping",
                columns: new[] { "BiometricDeviceId", "DeviceUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeviceMapping_BiometricDeviceId_EmployeeId",
                table: "EmployeeDeviceMapping",
                columns: new[] { "BiometricDeviceId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeviceMapping_EmployeeId",
                table: "EmployeeDeviceMapping",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceLogs");

            migrationBuilder.DropTable(
                name: "EmployeeDeviceMapping");

            migrationBuilder.DropTable(
                name: "BiometricDevices");
        }
    }
}
