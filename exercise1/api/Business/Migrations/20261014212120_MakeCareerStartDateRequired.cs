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
            // outside triggers, so a temp trigger on a temp table raises the error. It runs first,
            // before EF's table rebuild (which commits around its PRAGMA foreign_keys statements),
            // so nothing has been committed yet when it aborts.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE "__guard_CareerStartDate" ("x" INTEGER);
                CREATE TEMP TRIGGER "__guard_CareerStartDate_check" BEFORE INSERT ON "__guard_CareerStartDate"
                WHEN EXISTS (SELECT 1 FROM main."AstronautDetail" WHERE "CareerStartDate" IS NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'MakeCareerStartDateRequired: AstronautDetail has rows with NULL CareerStartDate. No data was changed. Set CareerStartDate on those rows first (back up the database before changing data). Only for a disposable development database: delete starbase.db and run dotnet ef database update.');
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
