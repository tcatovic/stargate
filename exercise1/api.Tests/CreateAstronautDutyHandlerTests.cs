using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StargateAPI.Business.Commands;

namespace StargateAPI.Tests;

/// <summary>
/// CreateAstronautDutyHandler against a migrated temp SQLite database seeded with DevelopmentSeedData
/// (Mira 1, Kenji 2, Tomas Lindqvist 3, Priya 4, Elena 5, Desmond 6). Expected values are written out, not read from the code.
/// </summary>
public class CreateAstronautDutyHandlerTests
{
    private const string NotFound = "Person not found";
    private const string Duplicate = "A duty with this title and start date already exists for this person";
    private const string Race = "The person's duties were changed by another request";

    private static async Task<CreateAstronautDutyResult> Create(
        TestDatabase db, string name, string rank, string title, DateTime start,
        ILogger<CreateAstronautDutyHandler>? logger = null, CancellationToken token = default, params IInterceptor[] interceptors)
    {
        using var context = db.CreateContext(interceptors);
        return await new CreateAstronautDutyHandler(context, logger ?? NullLogger<CreateAstronautDutyHandler>.Instance)
            .Handle(new CreateAstronautDuty { Name = name, Rank = rank, DutyTitle = title, DutyStartDate = start }, token);
    }

    private static string DutyRows(TestDatabase db, int personId) => string.Join("\n", db.Strings(
        $"SELECT \"Rank\" || '|' || \"DutyTitle\" || '|' || \"DutyStartDate\" || '|' || COALESCE(\"DutyEndDate\", '<null>') FROM \"AstronautDuty\" WHERE \"PersonId\" = {personId} ORDER BY \"DutyStartDate\", \"Id\""));

    private static string DetailRow(TestDatabase db, int personId) => string.Join("\n", db.Strings(
        $"SELECT \"CurrentRank\" || '|' || \"CurrentDutyTitle\" || '|' || \"CareerStartDate\" || '|' || COALESCE(\"CareerEndDate\", '<null>') FROM \"AstronautDetail\" WHERE \"PersonId\" = {personId}"));

    [Fact]
    public async Task CH1_AFirstDuty_Returns201_AndCreatesTheDetailAndTheOpenDuty()
    {
        using var db = TestDatabase.Seeded();

        var result = await Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.True(result.Success);
        Assert.Equal("Successful", result.Message);
        Assert.NotNull(result.Id);
        Assert.True(result.Id >= 1);
        Assert.Equal("Kenji Watanabe", result.Name);
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DutyRows(db, 2));
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DetailRow(db, 2));
        Assert.Equal(result.Id, (int)db.Scalar<long>("SELECT \"Id\" FROM \"AstronautDuty\" WHERE \"PersonId\" = 2"));
    }

    [Fact]
    public async Task CH2_AnUnknownPerson_Returns404_AndWritesNothing()
    {
        using var db = TestDatabase.Seeded();
        var before = db.Dump();

        var result = await Create(db, "Nobody", "CPT", "Pilot", new DateTime(2024, 1, 1));

        Assert.Equal(404, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal(NotFound, result.Message);
        Assert.Null(result.Id);
        Assert.Null(result.Name);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH3_Regression_ASecondDutyArrives_ClosesThePreviousDutyAndUpdatesTheDetail()
    {
        using var db = TestDatabase.Seeded();
        Assert.Equal("Tomas Lindqvist", db.Scalar<string>("SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = 3"));
        Assert.Equal("CPT|Pilot|2021-06-14 00:00:00|<null>", DutyRows(db, 3));
        var others = db.DumpExcept(3);

        // The time part of the request is discarded.
        var result = await Create(db, "Tomas Lindqvist", "MAJ", "Commander", new DateTime(2024, 3, 1, 15, 30, 0));

        Assert.Equal(201, result.ResponseCode);
        // The leap day: the previous duty ends on 2024-02-29.
        Assert.Equal(
            "CPT|Pilot|2021-06-14 00:00:00|2024-02-29 00:00:00\nMAJ|Commander|2024-03-01 00:00:00|<null>",
            DutyRows(db, 3));
        Assert.Equal("MAJ|Commander|2021-06-14 00:00:00|<null>", DetailRow(db, 3));
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"PersonId\" = 3 AND \"DutyEndDate\" IS NULL"));
        Assert.Equal(others, db.DumpExcept(3));
    }

    [Fact]
    public async Task CH4_Regression_ARetiredLaterDuty_SetsTheCareerEndToStartMinusOne_AndClosesOnlyTheLatestDuty()
    {
        using var db = TestDatabase.Seeded();
        var others = db.DumpExcept(5);

        var result = await Create(db, "Elena Vasquez", "MAJ", "RETIRED", new DateTime(2025, 1, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal(
            "2LT|Flight Engineer|2012-01-09 00:00:00|2016-02-29 00:00:00\n"
            + "CPT|Pilot|2016-03-01 00:00:00|2018-12-31 00:00:00\n"
            + "MAJ|Commander|2019-01-01 00:00:00|2024-12-31 00:00:00\n"
            + "MAJ|RETIRED|2025-01-01 00:00:00|<null>",
            DutyRows(db, 5));
        Assert.Equal("MAJ|RETIRED|2012-01-09 00:00:00|2024-12-31 00:00:00", DetailRow(db, 5));
        Assert.Equal(others, db.DumpExcept(5));
    }

    [Fact]
    public async Task CH5_Regression_AFirstDutyForAPersonWithoutAssignment_CreatesTheDetailWithoutACareerEnd()
    {
        using var db = TestDatabase.Seeded();
        Assert.Equal(0, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDetail\" WHERE \"PersonId\" = 1"));
        var others = db.DumpExcept(1);

        var result = await Create(db, "Mira Okafor", "CPT", "Pilot", new DateTime(2024, 1, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DetailRow(db, 1));
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DutyRows(db, 1));
        Assert.Equal(others, db.DumpExcept(1));
    }

    [Fact]
    public async Task CH6_TheDuplicateCheck_IsScopedToThePerson()
    {
        using var db = TestDatabase.Seeded();
        Assert.Equal(201, (await Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1))).ResponseCode);

        var other = await Create(db, "Mira Okafor", "CPT", "Pilot", new DateTime(2024, 1, 1));
        Assert.Equal(201, other.ResponseCode);

        var before = db.Dump();
        var same = await Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1));

        Assert.Equal(409, same.ResponseCode);
        Assert.False(same.Success);
        Assert.Equal(Duplicate, same.Message);
        Assert.Null(same.Id);
        Assert.Null(same.Name);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH7_TheDuplicateCheck_IgnoresTitleCase()
    {
        using var db = TestDatabase.Seeded();
        Assert.Equal(201, (await Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1))).ResponseCode);
        var before = db.Dump();

        var result = await Create(db, "Kenji Watanabe", "CPT", "pilot", new DateTime(2024, 1, 1));

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(Duplicate, result.Message);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH8_WhenTwoDutiesShareTheLatestStartDate_TheHigherIdIsClosed()
    {
        using var db = TestDatabase.Seeded();
        db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES "
            + "(2, 'CPT', 'First Inserted', '2024-01-01 00:00:00'), (2, 'CPT', 'Second Inserted', '2024-01-01 00:00:00')");

        var result = await Create(db, "Kenji Watanabe", "MAJ", "Commander", new DateTime(2024, 6, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal(
            "CPT|First Inserted|2024-01-01 00:00:00|<null>\n"
            + "CPT|Second Inserted|2024-01-01 00:00:00|2024-05-31 00:00:00\n"
            + "MAJ|Commander|2024-06-01 00:00:00|<null>",
            DutyRows(db, 2));
    }

    [Fact]
    public async Task CH9_AStoredStartDateWithATimePart_StillCountsAsTheSameDay()
    {
        using var db = TestDatabase.Seeded();
        db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES (2, 'CPT', 'Pilot', '2024-01-01 10:00:00')");
        var before = db.Dump();

        var result = await Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1));

        Assert.Equal(409, result.ResponseCode);
        Assert.Equal(Duplicate, result.Message);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH10_WhenAConcurrentFirstDutyWinsTheUniqueDetailIndex_TheResultIs409_NotAnException()
    {
        using var db = TestDatabase.Seeded();
        var intruder = new SqlOnAnotherConnectionBeforeSave(db.ConnectionString,
            "INSERT INTO \"AstronautDetail\" (\"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (1, 'LTC', 'Interloper', '2023-01-01 00:00:00'); "
            + "INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES (1, 'LTC', 'Interloper', '2023-01-01 00:00:00')");

        var result = await Create(db, "Mira Okafor", "CPT", "Pilot", new DateTime(2024, 1, 1), null, default, intruder);

        Assert.True(intruder.Ran);
        Assert.Equal(409, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal(Race, result.Message);
        Assert.Null(result.Id);
        // Only the rows the other writer inserted; this request's duty was rolled back with its detail.
        Assert.Equal("LTC|Interloper|2023-01-01 00:00:00|<null>", DetailRow(db, 1));
        Assert.Equal("LTC|Interloper|2023-01-01 00:00:00|<null>", DutyRows(db, 1));
    }

    [Fact]
    public async Task CH11_ValuesTravelOnlyAsParameters_OnTheEfCreatePath()
    {
        using var db = TestDatabase.Seeded();
        db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('zq'' OR Id IS 3 OR ''zq')");
        var counter = new CountingCommandInterceptor();

        var result = await Create(db, "zq' OR Id IS 3 OR 'zq", "CPT", "Pilotzq' OR '1", new DateTime(2024, 1, 1), null, default, counter);

        Assert.Equal(201, result.ResponseCode);
        Assert.NotEmpty(counter.Commands);
        Assert.All(counter.Commands, text => Assert.DoesNotContain("zq", text, StringComparison.OrdinalIgnoreCase));
        // And the values really were stored, verbatim.
        Assert.Equal("Pilotzq' OR '1", db.Scalar<string>("SELECT \"DutyTitle\" FROM \"AstronautDuty\" WHERE \"PersonId\" = 7"));
    }

    [Fact]
    public async Task CH12_AnAlreadyCancelledToken_Throws_AndWritesNothing()
    {
        using var db = TestDatabase.Seeded();
        var before = db.Dump();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1), null, cts.Token));

        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH12b_ATokenCancelledDuringTheSave_RollsEverythingBack()
    {
        using var db = TestDatabase.Seeded();
        var before = db.Dump();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(db, "Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1), null, cts.Token, new CancelDuringSave(cts)));

        Assert.Equal(before, db.Dump());
    }

    [Theory]
    [InlineData("x' OR '1'='1", "CPT", "Pilot", 2024)]
    [InlineData("Kenji Watanabe", "a;b", "Pilot", 2024)]
    [InlineData("Kenji Watanabe", "CPT", "", 2024)]
    [InlineData("Kenji Watanabe", "CPT", "Pilot", 1)]
    [InlineData("Kenji Watanabe", "CPT", "Pilot", 2101)]
    public async Task CH13_InvalidValuesPassedDirectly_ThrowAnArgumentExceptionThatDoesNotContainThem(string name, string rank, string title, int year)
    {
        using var db = TestDatabase.Seeded();
        var before = db.Dump();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Create(db, name, rank, title, new DateTime(year, 1, 1)));

        Assert.Equal("Invalid duty request. (Parameter 'request')", ex.Message);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CH14_Logging_NamesTheIdsAndNeverTheNameOrTitle()
    {
        using var db = TestDatabase.Seeded();
        var created = new CapturingLogger<CreateAstronautDutyHandler>();
        var rejected = new CapturingLogger<CreateAstronautDutyHandler>();

        var ok = await Create(db, "Kenji Watanabe", "CPT", "Secret Title", new DateTime(2024, 1, 1), created);
        await Create(db, "Nobody Special", "CPT", "Secret Title", new DateTime(2024, 1, 1), rejected);
        await Create(db, "Kenji Watanabe", "CPT", "Secret Title", new DateTime(2024, 1, 1), rejected);

        var entry = Assert.Single(created.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"Created duty {ok.Id} for person 2", entry.Message);
        Assert.Equal(
            ["Create duty rejected: person not found", "Create duty rejected: duplicate for person 2"],
            rejected.Entries.Select(e => e.Message).ToArray());
        foreach (var text in created.Entries.Concat(rejected.Entries).Select(e => e.Message))
        {
            Assert.DoesNotContain("Kenji", text);
            Assert.DoesNotContain("Nobody", text);
            Assert.DoesNotContain("Secret", text);
        }
    }

    [Theory]
    [InlineData("  kenji WATANABE\t")]
    [InlineData("KENJI WATANABE")]
    public async Task CH15_TheNameIsTrimmedAndMatchedIgnoringCase_AndRankAndTitleAreStoredTrimmed(string name)
    {
        using var db = TestDatabase.Seeded();

        var result = await Create(db, name, " MAJ ", "\tCommander ", new DateTime(2024, 1, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal("Kenji Watanabe", result.Name);
        Assert.Equal("MAJ|Commander|2024-01-01 00:00:00|<null>", DutyRows(db, 2));
        Assert.Equal("MAJ|Commander|2024-01-01 00:00:00|<null>", DetailRow(db, 2));
    }

    [Fact]
    public async Task CH16_APaddedRetiredTitle_CountsAsRetired()
    {
        using var db = TestDatabase.Seeded();

        var result = await Create(db, "Tomas Lindqvist", "CPT", " RETIRED ", new DateTime(2025, 1, 1));

        Assert.Equal(201, result.ResponseCode);
        Assert.Equal("CPT|RETIRED|2021-06-14 00:00:00|2024-12-31 00:00:00", DetailRow(db, 3));
    }
}
