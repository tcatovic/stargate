using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

public class CreatePersonHandlerTests
{
    private const string NameInUseMessage = "A person with this name already exists";

    private static TestDatabase SeededDatabase()
    {
        var db = new TestDatabase();
        db.Migrate();
        using var context = db.CreateContext();
        context.People.AddRange(DevelopmentSeedData.People());
        context.SaveChanges();
        return db;
    }

    private static long PersonCount(TestDatabase db) => db.Scalar<long>("SELECT COUNT(*) FROM \"Person\"");

    private static async Task<CreatePersonResult> Create(
        TestDatabase db, string name, ILogger<CreatePersonHandler>? logger = null, CancellationToken token = default, params IInterceptor[] interceptors)
    {
        using var context = db.CreateContext(interceptors);
        return await new CreatePersonHandler(context, logger ?? NullLogger<CreatePersonHandler>.Instance)
            .Handle(new CreatePerson { Name = name }, token);
    }

    [Fact]
    public async Task H1_CreatesAPerson_With201AndTheNewId()
    {
        using var db = SeededDatabase();

        var result = await Create(db, "Ada Lovelace");

        Assert.Equal(201, result.ResponseCode);
        Assert.True(result.Success);
        Assert.Equal("Successful", result.Message);
        Assert.Equal("Ada Lovelace", result.Name);
        Assert.NotNull(result.Id);
        Assert.True(result.Id >= 1);
        Assert.Equal(result.Id, (int)db.Scalar<long>("SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = 'Ada Lovelace'"));
        Assert.Equal(7, PersonCount(db));
    }

    [Theory]
    [InlineData("  Ada \t", "Ada")]
    [InlineData("\r\nAda\r\n", "Ada")]
    [InlineData(" Ada  Lovelace ", "Ada  Lovelace")]
    public async Task H2_TheNameIsTrimmedBeforeItIsStored(string input, string expected)
    {
        using var db = SeededDatabase();

        var result = await Create(db, input);

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal(expected, result.Name);
        Assert.Equal(expected, db.Scalar<string>($"SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = {result.Id}"));
    }

    [Theory]
    [InlineData(" Ada")]
    [InlineData("Ada　")]
    [InlineData("José")]
    [InlineData("Renée")]
    public async Task H3_NonAsciiIsNeverTrimmedOrStored(string name)
    {
        using var db = SeededDatabase();
        var before = db.Dump();

        await Assert.ThrowsAsync<ArgumentException>(() => Create(db, name));

        Assert.Equal(before, db.Dump());
    }

    [Theory]
    [InlineData("Mira Okafor")]
    [InlineData("mira okafor")]
    [InlineData("  MIRA OKAFOR ")]
    [InlineData("Mira Okafor\t")]
    public async Task H4_ADuplicateIsA409_FoundByThePreCheck_WithoutAnInsert(string name)
    {
        using var db = SeededDatabase();
        var before = db.Dump();
        var counter = new CountingCommandInterceptor();

        var result = await Create(db, name, null, default, counter);

        Assert.Equal(409, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal(NameInUseMessage, result.Message);
        Assert.Null(result.Id);
        Assert.Null(result.Name);
        Assert.Equal(before, db.Dump());
        Assert.NotEmpty(counter.Commands);
        Assert.DoesNotContain(counter.Commands, c => c.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task H5_ADifferentCasingOfANewlyCreatedName_Is409()
    {
        using var db = SeededDatabase();

        Assert.Equal(201, (await Create(db, "Ada Lovelace")).ResponseCode);
        var second = await Create(db, "ada lovelace");

        Assert.Equal(409, second.ResponseCode);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'ada lovelace' COLLATE NOCASE"));
    }

    [Fact]
    public async Task H6_WhenTheDatabaseRejectsALostRace_TheResultIs409_NotAnException()
    {
        using var db = SeededDatabase();
        var intruder = new SqlOnAnotherConnectionBeforeSave(db.ConnectionString, "INSERT INTO \"Person\" (\"Name\") VALUES ('Zed Unique')");

        // The pre-check passes (nobody has the name yet); another writer takes it just before this INSERT runs.
        var result = await Create(db, "zed unique", null, default, intruder);

        Assert.True(intruder.Ran);
        Assert.Equal(409, result.ResponseCode);
        Assert.Null(result.Id);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'zed unique' COLLATE NOCASE"));
        Assert.Equal("Zed Unique", db.Scalar<string>("SELECT \"Name\" FROM \"Person\" WHERE \"Name\" = 'zed unique' COLLATE NOCASE"));
        Assert.Equal(7, PersonCount(db));
    }

    [Fact]
    public async Task H7_AnyOtherDatabaseError_Propagates()
    {
        using var db = SeededDatabase();
        var thrower = new ThrowOnSave(new DbUpdateException("fk", new SqliteException("fk", 19, 787)));

        await Assert.ThrowsAsync<DbUpdateException>(() => Create(db, "Ada Lovelace", null, default, thrower));

        Assert.Equal(6, PersonCount(db));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<x>")]
    [InlineData("Secretvalue<x>")]
    public async Task H8_AnInvalidNamePassedDirectly_ThrowsArgumentException_WithoutTheValue_AndStoresNothing(string name)
    {
        using var db = SeededDatabase();
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Create(db, name));

        Assert.DoesNotContain("Secretvalue", ex.Message);
        Assert.DoesNotContain("<x>", ex.Message);
        Assert.Equal(before, db.Dump());
    }

    [Theory]
    [InlineData(101)]
    [InlineData(5000)]
    public async Task H8b_AnOverlongName_IsRejected(int length)
    {
        using var db = SeededDatabase();
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Create(db, new string('a', length)));

        Assert.DoesNotContain("aaaa", ex.Message);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task H9_CreatingAPerson_AddsNoAstronautRows_Rule2()
    {
        using var db = SeededDatabase();
        var before = db.Dump("AstronautDetail", "AstronautDuty");

        var result = await Create(db, "Ada Lovelace");

        Assert.Equal(before, db.Dump("AstronautDetail", "AstronautDuty"));
        Assert.Equal(0, db.Scalar<long>($"SELECT COUNT(*) FROM \"AstronautDetail\" WHERE \"PersonId\" = {result.Id}"));
        Assert.Equal(0, db.Scalar<long>($"SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"PersonId\" = {result.Id}"));
    }

    [Fact]
    public async Task H10_LogsTheIdAndTheOutcome_NeverTheName()
    {
        using var db = SeededDatabase();
        var logger = new CapturingLogger<CreatePersonHandler>();

        var created = await Create(db, "Ada Lovelace", logger);
        await Create(db, "Mira Okafor", logger);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Contains($"Created person {created.Id}", logger.Entries[0].Message);
        Assert.Contains("Create person rejected: name in use", logger.Entries[1].Message);
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain("Ada", entry.Message);
            Assert.DoesNotContain("Lovelace", entry.Message);
            Assert.DoesNotContain("Mira", entry.Message);
        }
    }

    [Fact]
    public async Task H11_ACancelledToken_IsHonoured_AndStoresNothing()
    {
        using var db = SeededDatabase();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(db, "Ada Lovelace", null, cts.Token));

        Assert.Equal(6, PersonCount(db));
    }

    [Fact]
    public async Task H11b_TheTokenIsPassedToTheSave()
    {
        using var db = SeededDatabase();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(db, "Ada Lovelace", null, cts.Token, new CancelDuringSave(cts)));

        Assert.Equal(6, PersonCount(db));
    }
}
