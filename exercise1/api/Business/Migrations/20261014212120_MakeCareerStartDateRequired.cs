using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StargateAPI.Migrations
{
    /// <inheritdoc />
    public partial class MakeCareerStartDateRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No backfill (task 003): abort, changing nothing, if any row lacks a CareerStartDate.
            // SQLite-specific and transitional (SQL Server replaces it later): SQLite has no RAISE
            // outside triggers, so a temp trigger on a temp table raises the error. It runs in the
            // same transaction as the table rebuild, so a failure rolls everything back.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE "__guard_CareerStartDate" ("x" INTEGER);
                CREATE TEMP TRIGGER "__guard_CareerStartDate_check" BEFORE INSERT ON "__guard_CareerStartDate"
                WHEN EXISTS (SELECT 1 FROM main."AstronautDetail" WHERE "CareerStartDate" IS NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'MakeCareerStartDateRequired: AstronautDetail has rows with NULL CareerStartDate. No data was changed. Delete starbase.db and run dotnet ef database update, or set CareerStartDate on those rows first.');
                END;
                INSERT INTO "__guard_CareerStartDate" ("x") VALUES (1);
                DROP TABLE "__guard_CareerStartDate";
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CareerStartDate",
                table: "AstronautDetail",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CareerStartDate",
                table: "AstronautDetail",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");
        }
    }
}
