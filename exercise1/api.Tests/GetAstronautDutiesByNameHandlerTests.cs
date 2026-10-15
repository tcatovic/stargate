using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

/// <summary>GetAstronautDutiesByNameHandler on a migrated temp database seeded with DevelopmentSeedData.</summary>
public class GetAstronautDutiesByNameHandlerTests
{
    private static async Task<GetAstronautDutiesByNameResult> Get(TestDatabase db, string name, CancellationToken token = default)
    {
        using var context = db.CreateContext();
        return await new GetAstronautDutiesByNameHandler(context).Handle(new GetAstronautDutiesByName { Name = name }, token);
    }

    [Fact]
    public async Task H1_AKnownName_ReturnsThePersonAndOnlyTheirDuties_NewestFirst()
    {
        using var db = TestDatabase.Seeded();

        var result = await Get(db, "Elena Vasquez");

        Assert.Equal(200, result.ResponseCode);
        Assert.True(result.Success);
        Assert.Equal("Successful", result.Message);
        Assert.NotNull(result.Person);
        Assert.Equal(5, result.Person.PersonId);
        Assert.Equal("Elena Vasquez", result.Person.Name);
        Assert.Equal("MAJ", result.Person.CurrentRank);
        Assert.Equal("Commander", result.Person.CurrentDutyTitle);
        Assert.Equal(["Commander", "Pilot", "Flight Engineer"], result.AstronautDuties.Select(d => d.DutyTitle).ToArray());
        Assert.All(result.AstronautDuties, d => Assert.Equal(5, d.PersonId));
        Assert.Null(result.AstronautDuties[0].DutyEndDate);
        Assert.Equal(new DateTime(2018, 12, 31), result.AstronautDuties[1].DutyEndDate);
        Assert.Equal(new DateTime(2012, 1, 9), result.AstronautDuties[2].DutyStartDate);
    }

    [Fact]
    public async Task H2_AnUnknownName_Is404_WithAFixedMessage_NoPerson_AndNoDuties()
    {
        using var db = TestDatabase.Seeded();

        var result = await Get(db, "Nobody Particular");

        Assert.Equal(404, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal("Person not found", result.Message);
        Assert.Null(result.Person);
        Assert.Empty(result.AstronautDuties);
    }

    [Fact]
    public async Task H3_APersonWithoutAnAssignment_Is200_WithEmptyStrings_NullDates_AndNoDuties()
    {
        using var db = TestDatabase.Seeded();

        var result = await Get(db, "Mira Okafor");

        Assert.Equal(200, result.ResponseCode);
        Assert.NotNull(result.Person);
        Assert.Equal("Mira Okafor", result.Person.Name);
        Assert.Equal("", result.Person.CurrentRank);
        Assert.Equal("", result.Person.CurrentDutyTitle);
        Assert.Null(result.Person.CareerStartDate);
        Assert.Null(result.Person.CareerEndDate);
        Assert.Empty(result.AstronautDuties);
    }

    [Theory]
    [InlineData(" elena VASQUEZ\t")]
    [InlineData("ELENA VASQUEZ")]
    public async Task H4_TheLookupIsTrimmedAndIgnoresAsciiCase(string name)
    {
        using var db = TestDatabase.Seeded();

        var result = await Get(db, name);

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Elena Vasquez", result.Person?.Name);
        Assert.Equal(3, result.AstronautDuties.Count);
    }

    [Theory]
    [InlineData("a' OR Id IS 3 OR 'a")]
    [InlineData("a' OR 1 OR 'a")]
    public async Task H5_AnInjectionShapedName_IsJustAName_AndNeverReturnsAnotherPersonsDuties(string name)
    {
        using var db = TestDatabase.Seeded();
        db.Execute($"INSERT INTO \"Person\" (\"Name\") VALUES ('{name.Replace("'", "''")}')");

        var result = await Get(db, name);

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal(name, result.Person?.Name);
        Assert.Equal(7, result.Person?.PersonId);
        Assert.Empty(result.AstronautDuties);

        // The same payload for a name nobody has is the unknown-person answer, not a wider result set.
        var unknown = await Get(db, name.Replace("a'", "b'").Replace("'a", "'b"));
        Assert.Equal(404, unknown.ResponseCode);
        Assert.Null(unknown.Person);
        Assert.Empty(unknown.AstronautDuties);
    }

    [Fact]
    public async Task H6_AnAlreadyCancelledToken_Throws()
    {
        using var db = TestDatabase.Seeded();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Get(db, "Elena Vasquez", cts.Token));
    }

    [Fact]
    public async Task H7_DutiesWithTheSameStartDate_ListTheHigherIdFirst()
    {
        using var db = TestDatabase.Seeded();
        db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES "
            + "(2, 'CPT', 'First Inserted', '2024-01-01 00:00:00'), (2, 'CPT', 'Second Inserted', '2024-01-01 00:00:00')");

        var result = await Get(db, "Kenji Watanabe");

        Assert.Equal(["Second Inserted", "First Inserted"], result.AstronautDuties.Select(d => d.DutyTitle).ToArray());
    }

    [Fact]
    public async Task H8_TheCaseInsensitiveMatch_DoesNotDependOnTheColumnCollation()
    {
        // Before AddUniquePersonNameIndex the Person.Name column has no NOCASE collation; the query must still fold case itself.
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.MakeCareerStartDateRequired);
        Assert.DoesNotContain("NOCASE", db.Scalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Person'"));
        db.Execute("INSERT INTO \"Person\" (\"Name\") VALUES ('Elena Vasquez')");

        var result = await Get(db, "elena VASQUEZ");

        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Elena Vasquez", result.Person?.Name);
    }
}
