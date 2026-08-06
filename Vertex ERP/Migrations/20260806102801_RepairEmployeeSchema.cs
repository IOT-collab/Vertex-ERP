using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vertex_ERP.Migrations
{
    /// <inheritdoc />
    public partial class RepairEmployeeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Employees'
                          AND column_name = 'UpdatedAt'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Employees'
                          AND column_name = 'UpdatedDate'
                    ) THEN
                        ALTER TABLE "Employees" RENAME COLUMN "UpdatedAt" TO "UpdatedDate";
                    ELSIF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Employees'
                          AND column_name = 'UpdatedDate'
                    ) THEN
                        ALTER TABLE "Employees" ADD COLUMN "UpdatedDate" timestamp with time zone NULL;
                    END IF;
                END $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Employees'
                          AND column_name = 'UpdatedDate'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Employees'
                          AND column_name = 'UpdatedAt'
                    ) THEN
                        ALTER TABLE "Employees" RENAME COLUMN "UpdatedDate" TO "UpdatedAt";
                    END IF;
                END $migration$;
                """);
        }
    }
}
