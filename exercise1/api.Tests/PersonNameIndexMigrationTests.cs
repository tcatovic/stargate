using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>Migration AddUniquePersonNameIndex: guard, trim, NOCASE column, unique index, Down.</summary>
public class PersonNameIndexMigrationTests
{
    private const string Development = "Development";

    private static SqliteException Sqlite(Exception error) =>
        Assert.IsType<SqliteException>(error is SqliteException ? error : error.InnerException);

    private static void InsertSeedPeople(TestDatabase db)
    {
        using var context = db.CreateContext();
        context.People.AddRange(DevelopmentSeedData.People());
        context.SaveChanges();
    }

    private static void AssertAbortedAndUntouched(TestDatabase db, Exception error, string dumpBefore)
    {
        var sqlite = Sqlite(error);
        Assert.Contains("AddUniquePersonNameIndex", sqlite.Message);
        Assert.Contains("No data was changed", sqlite.Message);
        Assert.Contains("back up", sqlite.Message);
        Assert.Equal(dumpBefore, db.Dump());
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.MakeCareerStartDateRequired }, db.AppliedMigrations());
        Assert.False(db.HasUniquePersonNameIndex());
        Assert.Null(db.PersonNameCollation());
        Assert.DoesNotContain("NOCASE", db.Scalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Person'"));
        Assert.Equal(0, db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'ef\\_temp\\_%' ESCAPE '\\' OR name LIKE '\\_\\_guard\\_%' ESCAPE '\\'"));
    }

    [Fact]
    public void M1_FreshDatabase_HasTheUniqueNocaseIndex_AndRejectsACaseVariant()
    {
        using var db = new TestDatabase();

        db.Migrate();

        Assert.True(db.HasUniquePersonNameIndex());
        Assert.Equal("NOCASE", db.PersonNameCollation());
        Assert.Equal(TestDatabase.AllMigrations, db.AppliedMigrations());
        Assert.Contains("\"Name\" TEXT COLLATE NOCASE NOT NULL", db.Scalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Person'"));
        db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('Ada');");
        var error = Assert.Throws<SqliteException>(() => db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('ADA');"));
        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Equal(2067, error.SqliteExtendedErrorCode);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }

    [Fact]
    public void M2_Upgrade_TrimsNames_KeepsIdsAndChildRows()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (4, '  Ada '), (5, 'Bob' || char(9)), (6, 'Cy'), (7, char(13) || char(10) || 'Di' || char(10)), (8, 'Eve  Jones');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"Id\", \"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (3, 5, 'CPT', 'Pilot', '2020-01-02 00:00:00');");
        db.Execute("INSERT INTO \"AstronautDuty\" (\"Id\", \"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\", \"DutyEndDate\") VALUES (11, 5, 'LT', 'Engineer', '2018-01-01 00:00:00', '2020-01-01 00:00:00'), (12, 5, 'CPT', 'Pilot', '2020-01-02 00:00:00', NULL);");
        var childrenBefore = db.Dump("AstronautDetail", "AstronautDuty");

        db.Migrate();

        Assert.Equal("4|Ada\n5|Bob\n6|Cy\n7|Di\n8|Eve  Jones", string.Join("\n", ReadPeople(db)));
        Assert.Equal(childrenBefore, db.Dump("AstronautDetail", "AstronautDuty"));
        Assert.Equal(TestDatabase.AllMigrations, db.AppliedMigrations());
        Assert.True(db.HasUniquePersonNameIndex());
        Assert.Equal("NOCASE", db.PersonNameCollation());
    }

    private static List<string> ReadPeople(TestDatabase db) =>
        db.Strings("SELECT \"Id\" || '|' || \"Name\" FROM \"Person\" ORDER BY \"Id\"");

    [Fact]
    public void M3_CaseOnlyDuplicates_AbortWithTheMessage_AndChangeNothing()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada'), (2, 'ada'), (3, 'Grace');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (3, 'CPT', 'Pilot', '2020-01-02 00:00:00');");
        var before = db.Dump();

        var error = Assert.ThrowsAny<Exception>(() => db.Migrate());

        AssertAbortedAndUntouched(db, error, before);
    }

    [Fact]
    public void M4_WhitespaceAndCaseDuplicates_AbortAndTheirPaddingIsKept()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada'), (2, '  ADA' || char(9)), (3, 'Grace');");
        var before = db.Dump();

        var error = Assert.ThrowsAny<Exception>(() => db.Migrate());

        AssertAbortedAndUntouched(db, error, before);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Id\" = 2 AND \"Name\" = '  ADA' || char(9)"));
    }

    [Fact]
    public void M4b_TrimmingAloneCreatesTheClash_StillAborts()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada'), (2, 'Ada ');");
        var before = db.Dump();

        var error = Assert.ThrowsAny<Exception>(() => db.Migrate());

        AssertAbortedAndUntouched(db, error, before);
    }

    [Fact]
    public void M5_Down_RemovesIndexAndCollation_KeepsRows_AndCanBeReapplied()
    {
        using var db = new TestDatabase();
        db.Migrate();
        InsertSeedPeople(db);
        var before = db.Dump();

        db.Migrate(TestDatabase.MakeCareerStartDateRequired);

        Assert.False(db.HasUniquePersonNameIndex());
        Assert.DoesNotContain("NOCASE", db.Scalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Person'"));
        Assert.Equal(before, db.Dump());
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.MakeCareerStartDateRequired }, db.AppliedMigrations());
        db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('Ada');");
        db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('ada');");
        db.Execute("DELETE FROM \"Person\" WHERE \"Name\" = 'ada';");

        db.Migrate();

        Assert.True(db.HasUniquePersonNameIndex());
        Assert.Equal(TestDatabase.AllMigrations, db.AppliedMigrations());
    }

    [Fact]
    public void M6_SeedDataAtThePreviousMigration_Upgrades()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        InsertSeedPeople(db);
        var before = db.Dump();

        db.Migrate();

        Assert.Equal(6, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task M8_DevelopmentStartupOnADuplicateDatabase_FailsAndSeedsNothing()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada'), (2, 'ada');");
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.CreateInitializer(Development).InitializeAsync());

        Assert.Contains("migration", ex.Message);
        Assert.Equal(before, db.Dump());
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.MakeCareerStartDateRequired }, db.AppliedMigrations());
        Assert.Equal(2, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }
}
