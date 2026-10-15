using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

public class UpdatePersonHandlerTests
{
    private const string Children = "AstronautDetail";

    private static TestDatabase SeededDatabase()
    {
        var db = new TestDatabase();
        db.Migrate();
        using var context = db.CreateContext();
        context.People.AddRange(DevelopmentSeedData.People());
        context.SaveChanges();
        return db;
    }

    private static int IdOf(TestDatabase db, string name) =>
        (int)db.Scalar<long>($"SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = '{name}'");

    private static string StoredName(TestDatabase db, int id) =>
        db.Scalar<string>($"SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = {id}");

    private static async Task<UpdatePersonResult> Rename(TestDatabase db, int id, string name, ILogger<UpdatePersonHandler>? logger = null, params IInterceptor[] interceptors)
    {
        using var context = db.CreateContext(interceptors);
        return await new UpdatePersonHandler(context, logger ?? NullLogger<UpdatePersonHandler>.Instance)
            .Handle(new UpdatePerson { PersonId = id, Name = name }, CancellationToken.None);
    }

    private static string Detail(TestDatabase db) => db.Dump("AstronautDetail", "AstronautDuty");

    [Fact]
    public async Task H1_RenamesAnUnassignedPerson_AndOnlyThatPerson()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var childrenBefore = db.Dump("AstronautDetail", "AstronautDuty");
        var othersBefore = db.Strings($"SELECT \"Id\" || '|' || \"Name\" FROM \"Person\" WHERE \"Id\" <> {mira} ORDER BY \"Id\"");

        var result = await Rename(db, mira, "Mira Okafor-Lindqvist");

        Assert.True(result.Success);
        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Successful", result.Message);
        Assert.NotNull(result.Person);
        Assert.Equal(mira, result.Person.PersonId);
        Assert.Equal("Mira Okafor-Lindqvist", result.Person.Name);
        Assert.Equal("", result.Person.CurrentRank);
        Assert.Equal("", result.Person.CurrentDutyTitle);
        Assert.Null(result.Person.CareerStartDate);
        Assert.Null(result.Person.CareerEndDate);
        Assert.Equal("Mira Okafor-Lindqvist", StoredName(db, mira));
        Assert.Equal(childrenBefore, db.Dump("AstronautDetail", "AstronautDuty"));
        Assert.Equal(othersBefore, db.Strings($"SELECT \"Id\" || '|' || \"Name\" FROM \"Person\" WHERE \"Id\" <> {mira} ORDER BY \"Id\""));
    }

    [Fact]
    public async Task H2_RenamingAnAssignedPerson_LeavesDetailAndDutiesUntouched()
    {
        using var db = SeededDatabase();
        var tomas = IdOf(db, "Tomas Lindqvist");
        var before = Detail(db);

        var result = await Rename(db, tomas, "Tomas Lindqvist-Berg");

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal(before, Detail(db));
        Assert.Equal("CPT", result.Person?.CurrentRank);
        Assert.Equal("Pilot", result.Person?.CurrentDutyTitle);
        Assert.Equal(new DateTime(2021, 6, 14), result.Person?.CareerStartDate);
        Assert.Null(result.Person?.CareerEndDate);
        Assert.Equal(4, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\""));
        Assert.Equal(8, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDuty\""));
    }

    [Fact]
    public async Task H3_SurroundingWhitespaceIsRemoved_InnerWhitespaceIsKept()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");

        var result = await Rename(db, mira, "  Tomas  Lindqvist-Berg \t");

        Assert.Equal("Tomas  Lindqvist-Berg", StoredName(db, mira));
        Assert.Equal("Tomas  Lindqvist-Berg", result.Person?.Name);
    }

    [Fact]
    public async Task H4_AnNfdNameIsStoredAsNfc()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");

        var result = await Rename(db, mira, "Rene\u0301e O\u0308degaard");

        var stored = StoredName(db, mira);
        Assert.Equal(new[] { 'R', 'e', 'n', '\u00E9', 'e', ' ', '\u00D6', 'd', 'e', 'g', 'a', 'a', 'r', 'd' }, stored.ToCharArray());
        Assert.Equal(stored, result.Person?.Name);
    }

    [Fact]
    public async Task H5_RenamingToTheOwnExactName_Succeeds_AndChangesNothing()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var before = db.Dump();

        var result = await Rename(db, mira, "Mira Okafor");

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Mira Okafor", result.Person?.Name);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task H6_RenamingToADifferentCasingOfTheOwnName_Succeeds_AndStoresIt()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");

        var result = await Rename(db, mira, "MIRA okafor");

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("MIRA okafor", StoredName(db, mira));
        Assert.Equal("MIRA okafor", result.Person?.Name);
    }

    [Fact]
    public async Task H7_TheSameRequestTwice_GivesTheSameResultAndState()
    {
        using var db = SeededDatabase();
        var kenji = IdOf(db, "Kenji Watanabe");

        var first = await Rename(db, kenji, " Kenji Watanabe-Ito ");
        var afterFirst = db.Dump();
        var second = await Rename(db, kenji, " Kenji Watanabe-Ito ");

        Assert.Equal(200, first.ResponseCode);
        Assert.Equal(200, second.ResponseCode);
        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.Success, second.Success);
        Assert.Equal(kenji, second.Person?.PersonId);
        Assert.Equal("Kenji Watanabe-Ito", second.Person?.Name);
        Assert.Equal(first.Person?.Name, second.Person?.Name);
        Assert.Equal(afterFirst, db.Dump());
    }

    [Fact]
    public async Task H8_AnUnknownId_Is404_AndChangesNothing()
    {
        using var db = SeededDatabase();
        var before = db.Dump();

        var result = await Rename(db, 999999, "Nobody Here");

        Assert.False(result.Success);
        Assert.Equal(404, result.ResponseCode);
        Assert.Equal("Person not found", result.Message);
        Assert.Null(result.Person);
        Assert.Equal(before, db.Dump());
    }

    [Theory]
    [InlineData("Mira Okafor")]
    [InlineData("mira okafor")]
    [InlineData("  MIRA OKAFOR  ")]
    [InlineData("Mira Okafor\t")]
    public async Task H9_ANameTakenByAnotherPerson_Is409_ignoringCaseAndWhitespace(string name)
    {
        using var db = SeededDatabase();
        var kenji = IdOf(db, "Kenji Watanabe");
        var before = db.Dump();

        var result = await Rename(db, kenji, name);

        Assert.False(result.Success);
        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("A person with this name already exists", result.Message);
        Assert.Null(result.Person);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task H10_NfdVariantsConflictWithNfcNames_ButNonAsciiCaseVariantsDoNot()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var kenji = IdOf(db, "Kenji Watanabe");
        Assert.Equal(200, (await Rename(db, mira, "Ren\u00E9e")).ResponseCode);

        Assert.Equal(409, (await Rename(db, kenji, "Rene\u0301e")).ResponseCode);
        Assert.Equal(409, (await Rename(db, kenji, "rEne\u0301e")).ResponseCode);
        Assert.Equal("Kenji Watanabe", StoredName(db, kenji));

        // SQLite NOCASE folds ASCII only: a capital E-acute is a different name (documented limit).
        Assert.Equal(200, (await Rename(db, kenji, "REN\u00C9E")).ResponseCode);
    }

    [Fact]
    public async Task H11_WhenTheDatabaseRejectsALostRace_TheResultIs409_NotAnException()
    {
        using var db = SeededDatabase();
        var priya = IdOf(db, "Priya Raman");
        var kenji = IdOf(db, "Kenji Watanabe");
        var intruder = new RenameOnAnotherConnection(db.ConnectionString, $"UPDATE \"Person\" SET \"Name\" = 'Zed Unique' WHERE \"Id\" = {priya}");

        // The pre-check passes (nobody has the name yet); Priya takes it just before Kenji's UPDATE runs.
        var result = await Rename(db, kenji, "zed unique", null, intruder);

        Assert.True(intruder.Ran);
        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("A person with this name already exists", result.Message);
        Assert.Null(result.Person);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'zed unique' COLLATE NOCASE"));
        Assert.Equal(priya, (int)db.Scalar<long>("SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = 'zed unique' COLLATE NOCASE"));
        Assert.Equal("Kenji Watanabe", StoredName(db, kenji));
    }

    [Fact]
    public void H12_OnlyAUniqueConstraintViolationIsRecognised()
    {
        Assert.False(SqliteErrors.IsUniqueViolation(new DbUpdateException("x", new SqliteException("fk", 19, 787))));
        Assert.False(SqliteErrors.IsUniqueViolation(new DbUpdateException("x")));
        Assert.False(SqliteErrors.IsUniqueViolation(new DbUpdateException("x", new InvalidOperationException("other"))));
        Assert.True(SqliteErrors.IsUniqueViolation(new DbUpdateException("x", new SqliteException("unique", 19, 2067))));
    }

    [Fact]
    public async Task H12b_AForeignKeyFailureIsNotMappedTo409()
    {
        using var db = SeededDatabase();
        var kenji = IdOf(db, "Kenji Watanabe");
        var thrower = new ThrowOnSave(new DbUpdateException("fk", new SqliteException("fk", 19, 787)));

        await Assert.ThrowsAsync<DbUpdateException>(() => Rename(db, kenji, "Kenji Other", null, thrower));

        Assert.Equal("Kenji Watanabe", StoredName(db, kenji));
    }

    [Theory]
    [InlineData("<x>")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada\u3164")]
    [InlineData("Ada\u180E")]
    [InlineData("\u180EAda")]
    public async Task H13_AnInvalidNamePassedDirectly_ThrowsArgumentException_AndChangesNothing(string name)
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Rename(db, mira, name));

        Assert.DoesNotContain("<x>", ex.Message);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task H14_LogsTheIdAndOutcome_NeverTheName()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var kenji = IdOf(db, "Kenji Watanabe");
        var logger = new CapturingLogger<UpdatePersonHandler>();

        await Rename(db, mira, "Secretname Renamed", logger);
        await Rename(db, kenji, "secretname renamed", logger);
        await Rename(db, 999999, "Secretname Other", logger);

        Assert.Equal(3, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Contains($"Renamed person {mira}", logger.Entries[0].Message);
        Assert.Contains($"Rename of person {kenji} rejected: name in use", logger.Entries[1].Message);
        Assert.Contains("Rename of person 999999 rejected: not found", logger.Entries[2].Message);
        Assert.All(logger.Entries, e => Assert.DoesNotContain("ecretname", e.Message));
    }

    [Fact]
    public async Task H15_ACancelledTokenThrows_AndChangesNothing()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var before = db.Dump();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var context = db.CreateContext();
        var handler = new UpdatePersonHandler(context, NullLogger<UpdatePersonHandler>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new UpdatePerson { PersonId = mira, Name = "Mira Renamed" }, cts.Token));

        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task H16_ARowDeletedBetweenLoadAndSave_Is404_NotAnException()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var deleter = new RenameOnAnotherConnection(db.ConnectionString, $"DELETE FROM \"Person\" WHERE \"Id\" = {mira}");

        var result = await Rename(db, mira, "Mira Renamed", null, deleter);

        Assert.True(deleter.Ran);
        Assert.Equal(404, result.ResponseCode);
        Assert.Equal("Person not found", result.Message);
        Assert.False(result.Success);
        Assert.Null(result.Person);
        Assert.Equal(0, db.Scalar<long>($"SELECT COUNT(*) FROM \"Person\" WHERE \"Id\" = {mira}"));
        Assert.Equal(5, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }

    [Fact]
    public async Task H17_ARowDeletedAfterTheSaveButBeforeTheReadBack_Is404_NotA200WithNoPerson()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var deleter = new DeleteAfterSave(db.ConnectionString, $"DELETE FROM \"Person\" WHERE \"Id\" = {mira}");

        var result = await Rename(db, mira, "Mira Renamed", null, deleter);

        Assert.True(deleter.Ran);
        Assert.Equal(404, result.ResponseCode);
        Assert.Equal("Person not found", result.Message);
        Assert.False(result.Success);
        Assert.Null(result.Person);
        Assert.Equal(0, db.Scalar<long>($"SELECT COUNT(*) FROM \"Person\" WHERE \"Id\" = {mira}"));
    }

    [Fact]
    public async Task H18_AnOverlongRawName_IsRejectedWithoutBeingNormalised()
    {
        using var db = SeededDatabase();
        var mira = IdOf(db, "Mira Okafor");
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Rename(db, mira, new string('a', 801)));

        Assert.DoesNotContain("aaaa", ex.Message);
        Assert.Equal(before, db.Dump());
        // 800 raw characters is within the raw bound but still not a valid name (over 100 after normalising).
        await Assert.ThrowsAsync<ArgumentException>(() => Rename(db, mira, new string('a', 800)));
        Assert.Equal(before, db.Dump());
    }

    private sealed class DeleteAfterSave(string connectionString, string sql) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Ran = true;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class RenameOnAnotherConnection(string connectionString, string sql) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowOnSave(Exception exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw exception;
    }
}
