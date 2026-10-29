using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class FeedbackDocumentExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PublishedByUserId = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackDocuments_Users_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackDocumentSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackDocumentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackDocumentSubmissions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackDocumentSubmissions_FeedbackDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "FeedbackDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackDocumentSubmissions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackDocumentAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackDocumentAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackDocumentAssets_FeedbackDocumentSubmissions_Submissi~",
                        column: x => x.SubmissionId,
                        principalTable: "FeedbackDocumentSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackDocumentAssets_FeedbackDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "FeedbackDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentAssets_DocumentId",
                table: "FeedbackDocumentAssets",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentAssets_StoredFileName",
                table: "FeedbackDocumentAssets",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentAssets_SubmissionId",
                table: "FeedbackDocumentAssets",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocuments_PublishedAtUtc",
                table: "FeedbackDocuments",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocuments_PublishedByUserId",
                table: "FeedbackDocuments",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentSubmissions_DocumentId_EmployeeId",
                table: "FeedbackDocumentSubmissions",
                columns: new[] { "DocumentId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentSubmissions_EmployeeId",
                table: "FeedbackDocumentSubmissions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackDocumentSubmissions_SubmittedByUserId",
                table: "FeedbackDocumentSubmissions",
                column: "SubmittedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackDocumentAssets");

            migrationBuilder.DropTable(
                name: "FeedbackDocumentSubmissions");

            migrationBuilder.DropTable(
                name: "FeedbackDocuments");
        }
    }
}
