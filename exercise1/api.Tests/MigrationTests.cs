using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace StargateAPI.Tests;

public class MigrationTests
{
    private const string InsertPerson = "INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada');";

    [Fact]
    public void Model_has_no_pending_changes_against_the_migration_chain()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migrating_a_fresh_database_creates_the_schema_and_constraints()
    {
        using var db = new TestDatabase();

        db.Migrate();

        var tables = db.Strings("SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("Person", tables);
        Assert.Contains("AstronautDetail", tables);
        Assert.Contains("AstronautDuty", tables);
        foreach (var table in new[] { "Person", "AstronautDetail", "AstronautDuty" })
        {
            Assert.Equal(new[] { "Id" }, db.Strings($"SELECT name FROM pragma_table_info('{table}') WHERE pk > 0"));
        }
        Assert.True(db.CareerStartDateIsNotNull());
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM pragma_foreign_key_list('AstronautDetail') WHERE \"table\" = 'Person' AND on_delete = 'CASCADE'"));
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM pragma_foreign_key_list('AstronautDuty') WHERE \"table\" = 'Person' AND on_delete = 'CASCADE'"));
        Assert.Equal(1, db.Scalar<long>("SELECT \"unique\" FROM pragma_index_list('AstronautDetail') WHERE name = 'IX_AstronautDetail_PersonId'"));
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.LatestMigration }, db.AppliedMigrations());
    }

    [Fact]
    public void Migrating_again_changes_nothing()
    {
        using var db = new TestDatabase();
        db.Migrate();
        db.Execute(InsertPerson);
        var history = db.AppliedMigrations();

        db.Migrate();

        Assert.Equal(history, db.AppliedMigrations());
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }

    [Fact]
    public void Existing_rows_survive_the_upgrade_from_InitialCreate()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);
        db.Execute(InsertPerson);
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (7, 1, 'Captain', 'Pilot', '2020-01-02 00:00:00');");

        db.Migrate();

        Assert.True(db.CareerStartDateIsNotNull());
        Assert.Equal("2020-01-02 00:00:00", db.Scalar<string>("SELECT \"CareerStartDate\" FROM \"AstronautDetail\" WHERE \"Id\" = 7 AND \"PersonId\" = 1"));
        Assert.Equal("Captain", db.Scalar<string>("SELECT \"CurrentRank\" FROM \"AstronautDetail\" WHERE \"Id\" = 7"));
        Assert.Equal("Pilot", db.Scalar<string>("SELECT \"CurrentDutyTitle\" FROM \"AstronautDetail\" WHERE \"Id\" = 7"));
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\""));
    }

    [Fact]
    public void Upgrade_from_an_empty_InitialCreate_database_succeeds()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);

        db.Migrate();

        Assert.True(db.CareerStartDateIsNotNull());
    }

    [Fact]
    public void Null_CareerStartDate_aborts_the_migration_with_a_clear_message_and_changes_nothing()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);
        db.Execute(InsertPerson);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (2, 'Grace');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (1, 1, 'Captain', 'Pilot', NULL);");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (2, 2, 'Major', 'Engineer', '2021-05-06 00:00:00');");

        var error = Assert.ThrowsAny<Exception>(() => db.Migrate());

        var sqlite = Assert.IsType<SqliteException>(error is SqliteException ? error : error.InnerException);
        Assert.Contains("NULL CareerStartDate", sqlite.Message);
        Assert.Contains("No data was changed", sqlite.Message);
        Assert.Contains("back up", sqlite.Message);
        Assert.False(db.CareerStartDateIsNotNull());
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\" WHERE \"Id\" = 1 AND \"CareerStartDate\" IS NULL"));
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\" WHERE \"Id\" = 2 AND \"CareerStartDate\" = '2021-05-06 00:00:00'"));
        Assert.Equal(new[] { TestDatabase.InitialCreate }, db.AppliedMigrations());
        Assert.Equal(0, db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'ef\\_temp\\_%' ESCAPE '\\' OR name LIKE '\\_\\_guard\\_%' ESCAPE '\\'"));
    }

    [Fact]
    public void Down_makes_the_column_nullable_again_keeps_rows_and_can_be_reapplied()
    {
        using var db = new TestDatabase();
        db.Migrate();
        db.Execute(InsertPerson);
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (7, 1, 'Captain', 'Pilot', '2020-01-02 00:00:00');");

        db.Migrate(TestDatabase.InitialCreate);

        Assert.False(db.CareerStartDateIsNotNull());
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\" WHERE \"Id\" = 7"));
        Assert.Equal(new[] { TestDatabase.InitialCreate }, db.AppliedMigrations());
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (2, 'Grace');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (8, 2, 'Major', 'Engineer', NULL);");
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM pragma_foreign_key_list('AstronautDetail') WHERE \"table\" = 'Person' AND on_delete = 'CASCADE'"));
        Assert.Equal(1, db.Scalar<long>("SELECT \"unique\" FROM pragma_index_list('AstronautDetail') WHERE name = 'IX_AstronautDetail_PersonId'"));
        db.Execute("DELETE FROM \"AstronautDetail\" WHERE \"Id\" = 8;"); // the NULL row would (rightly) block re-applying

        db.Migrate();

        Assert.True(db.CareerStartDateIsNotNull());
    }
}
