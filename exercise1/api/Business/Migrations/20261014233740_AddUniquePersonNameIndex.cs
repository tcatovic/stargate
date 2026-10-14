using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StargateAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePersonNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rule 1 (task 002): abort, changing nothing, if names collide ignoring ASCII case and surrounding whitespace.
            // Must run before EF's table rebuild, which commits around PRAGMA foreign_keys: EF also creates the unique index on the
            // old BINARY column first and the NOCASE index only after the committed rebuild, so without this guard case-only
            // duplicates would fail half-way. SQLite-specific and transitional (same pattern as MakeCareerStartDateRequired).
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE "__guard_PersonName" ("x" INTEGER);
                CREATE TEMP TRIGGER "__guard_PersonName_check" BEFORE INSERT ON "__guard_PersonName"
                WHEN EXISTS (SELECT 1 FROM main."Person" GROUP BY trim("Name", char(32, 9, 10, 13)) COLLATE NOCASE HAVING COUNT(*) > 1)
                BEGIN
                    SELECT RAISE(ABORT, 'AddUniquePersonNameIndex: some Person names are equal ignoring ASCII case and leading or trailing whitespace, so the unique index on Person.Name cannot be created. No data was changed. List them with: SELECT trim(Name, char(32,9,10,13)) COLLATE NOCASE AS n, group_concat(Id) FROM Person GROUP BY n HAVING COUNT(*) > 1; then rename or merge those people (back up the database before changing data). Only for a disposable development database: delete starbase.db and restart.');
                END;
                INSERT INTO "__guard_PersonName" ("x") VALUES (1);
                DROP TABLE "__guard_PersonName";
                """);

            // Names are trimmed before they are stored (task 002 decision). Safe: the guard proved no collision after trimming.
            migrationBuilder.Sql("""UPDATE "Person" SET "Name" = trim("Name", char(32, 9, 10, 13)) WHERE "Name" <> trim("Name", char(32, 9, 10, 13));""");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Person",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Person_Name",
                table: "Person",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Person_Name",
                table: "Person");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Person",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldCollation: "NOCASE");
        }
    }
}
