using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddWdmsEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BiometricEnrollments",
                columns: table => new
                {
                    EnrollCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    WdmsName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsSheetReference = table.Column<bool>(type: "boolean", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiometricEnrollments", x => x.EnrollCode);
                    table.ForeignKey(
                        name: "FK_BiometricEnrollments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BiometricEnrollments_EmployeeId",
                table: "BiometricEnrollments",
                column: "EmployeeId");
            // Reference supplied by HR: ERP employee updated sheet (1).xlsx, Sheet1 A2:B65.
            // One-time seed; subsequent WDMS sync preserves these authoritative source names.
            migrationBuilder.InsertData(
                table: "BiometricEnrollments",
                columns: new[] { "EnrollCode", "FullName", "IsSheetReference", "Status" },
                values: new object[,]
                {
                    { "5", "Amit Kumar Bhati", true, "Pending" },
                    { "8", "Arun", true, "Pending" },
                    { "11", "Bani Singh", true, "Pending" },
                    { "12", "Braham Singh Chauhan", true, "Pending" },
                    { "13", "Chandra Bhan Singh", true, "Pending" },
                    { "14", "Dashrath kumar Yadav", true, "Pending" },
                    { "15", "Dhanveer Yadav", true, "Pending" },
                    { "16", "Gaurav Anand", true, "Pending" },
                    { "18", "Govind", true, "Pending" },
                    { "22", "Kapil", true, "Pending" },
                    { "24", "Kuldeep kumar", true, "Pending" },
                    { "25", "Kush Kumar", true, "Pending" },
                    { "28", "Manoj Kumar", true, "Pending" },
                    { "29", "Mohd Adnan", true, "Pending" },
                    { "31", "Naveen Kumar", true, "Pending" },
                    { "32", "Pankaj Kumar", true, "Pending" },
                    { "36", "Prem Pal", true, "Pending" },
                    { "40", "Rahul Kumar", true, "Pending" },
                    { "43", "Raj Kumar", true, "Pending" },
                    { "44", "Rajkumar", true, "Pending" },
                    { "45", "Rajnish Sharma", true, "Pending" },
                    { "46", "Ramesh kumar Yadav", true, "Pending" },
                    { "47", "Ranveer kumar Yadav", true, "Pending" },
                    { "48", "Rishabh Vishwakarma", true, "Pending" },
                    { "53", "Sakshi Katariya", true, "Pending" },
                    { "54", "Sanoj Kumar", true, "Pending" },
                    { "55", "Sarvesh Yadav", true, "Pending" },
                    { "56", "Satya Kumar", true, "Pending" },
                    { "59", "Shriram Sharma", true, "Pending" },
                    { "61", "Sonu", true, "Pending" },
                    { "64", "Suraj Pal", true, "Pending" },
                    { "66", "Sushil Kumar Tyagi", true, "Pending" },
                    { "69", "Updesh Kumar", true, "Pending" },
                    { "71", "Vikram Singh", true, "Pending" },
                    { "74", "Yuvraj Singh", true, "Pending" },
                    { "76", "Sourabh Saini", true, "Pending" },
                    { "79", "Ashish Srivastava", true, "Pending" },
                    { "85", "Chetan Sharma", true, "Pending" },
                    { "89", "Gaurav Kumar", true, "Pending" },
                    { "90", "Amit Kumar", true, "Pending" },
                    { "91", "Gourav Kumar", true, "Pending" },
                    { "92", "Tinku", true, "Pending" },
                    { "93", "Nihal", true, "Pending" },
                    { "99", "Prabhakar", true, "Pending" },
                    { "108", "Ashwani Kumar", true, "Pending" },
                    { "109", "Wasimuddin", true, "Pending" },
                    { "111", "Sonali Kushwah", true, "Pending" },
                    { "112", "Nishu", true, "Pending" },
                    { "117", "Krishna Kumar Gaud", true, "Pending" },
                    { "119", "Sanjeev Kumar", true, "Pending" },
                    { "120", "Peeyush Shrivastav", true, "Pending" },
                    { "122", "Brij pal", true, "Pending" },
                    { "123", "Sunny", true, "Pending" },
                    { "126", "Jaya Pal", true, "Pending" },
                    { "130", "Pradeep Kumar", true, "Pending" },
                    { "131", "Vishal Pal", true, "Pending" },
                    { "132", "Karthik Sharma", true, "Pending" },
                    { "133", "Shreya Raj", true, "Pending" },
                    { "135", "Rajeev Kumar Prajapati", true, "Pending" },
                    { "136", "Vikky Kumar", true, "Pending" },
                    { "138", "Karuna Rai", true, "Pending" },
                    { "139", "Bhoopendra Singh", true, "Pending" },
                    { "140", "Deepa Katariya", true, "Pending" },
                    { "141", "Priya Srivastava", true, "Pending" }
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BiometricEnrollments");
        }
    }
}
