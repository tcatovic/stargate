using StargateAPI.Business.Data;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

public class GetPeopleQueryTests
{
    private static async Task<GetPeopleResult> Run(TestDatabase db, GetPeople request)
    {
        using var context = db.CreateContext();
        return await new GetPeopleHandler(context).Handle(request, CancellationToken.None);
    }

    private static HashSet<string> Names(GetPeopleResult result) => result.People.Select(p => p.Name).ToHashSet();

    [Fact]
    public async Task P1_NoFilters_ReturnsEveryoneAndKeepsUnassignedShape()
    {
        using var db = PeopleFixture.Create();

        var result = await Run(db, new GetPeople());

        Assert.Equal(200, result.ResponseCode);
        Assert.True(result.Success);
        Assert.Equal(PeopleFixture.AllNames.ToHashSet(), Names(result));
        foreach (var name in new[] { "Mira Okafor", "Kenji Watanabe" })
        {
            var person = result.People.Single(p => p.Name == name);
            Assert.Equal("", person.CurrentRank);
            Assert.Equal("", person.CurrentDutyTitle);
            Assert.Null(person.CareerStartDate);
            Assert.Null(person.CareerEndDate);
        }
    }

    [Theory]
    [InlineData("ra", new[] { "Mira Okafor", "Priya Raman" })]
    [InlineData("ELENA", new[] { "Elena Vasquez" })]
    [InlineData("okafor", new[] { "Mira Okafor" })]
    public async Task P2_NameIsCaseInsensitiveContains(string name, string[] expected)
    {
        using var db = PeopleFixture.Create();
        Assert.Equal(expected.ToHashSet(), Names(await Run(db, new GetPeople { Name = name })));
    }

    [Theory]
    [InlineData("maj", new[] { "Priya Raman", "Elena Vasquez" })]
    [InlineData("MA", new string[0])]
    [InlineData("MAJ ", new[] { "Priya Raman", "Elena Vasquez" })]
    public async Task P3_RankIsCaseInsensitiveExactAndTrimmed(string rank, string[] expected)
    {
        using var db = PeopleFixture.Create();
        Assert.Equal(expected.ToHashSet(), Names(await Run(db, new GetPeople { Rank = rank })));
    }

    [Theory]
    [InlineData("mission specialist", new[] { "Priya Raman" })]
    [InlineData("Mission", new string[0])]
    [InlineData("retired", new[] { "Desmond Achebe" })]
    public async Task P4_DutyTitleIsCaseInsensitiveExact(string title, string[] expected)
    {
        using var db = PeopleFixture.Create();
        Assert.Equal(expected.ToHashSet(), Names(await Run(db, new GetPeople { DutyTitle = title })));
    }

    [Fact]
    public async Task P5_FiltersCombineWithAnd()
    {
        using var db = PeopleFixture.Create();

        Assert.Equal(["Elena Vasquez"], Names(await Run(db, new GetPeople { Rank = "MAJ", DutyTitle = "commander" })));
        Assert.Equal(["Priya Raman"], Names(await Run(db, new GetPeople { Name = "priya", Rank = "maj" })));
        Assert.Empty((await Run(db, new GetPeople { Name = "elena", DutyTitle = "Pilot" })).People);
    }

    [Fact]
    public async Task P6_NoMatch_IsAnEmptyListWith200()
    {
        using var db = PeopleFixture.Create();

        var result = await Run(db, new GetPeople { Name = "Nobody" });

        Assert.NotNull(result.People);
        Assert.Empty(result.People);
        Assert.Equal(200, result.ResponseCode);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task P7_UnassignedPeopleAreNeverMatchedByRankOrDutyTitle_AndEmptyFilterIsNoFilter()
    {
        using var db = PeopleFixture.Create();

        Assert.Empty((await Run(db, new GetPeople { Name = "Mira", Rank = "CPT" })).People);
        // An empty-string rank must not match the unassigned people's "" either: it means no filter.
        Assert.Equal(PeopleFixture.AllNames.ToHashSet(), Names(await Run(db, new GetPeople { Rank = "" })));
        var kenji = (await Run(db, new GetPeople { Name = "kenji" })).People.Single();
        Assert.Equal("Kenji Watanabe", kenji.Name);
        Assert.Equal("", kenji.CurrentRank);
        Assert.Equal("", kenji.CurrentDutyTitle);
    }

    [Fact]
    public async Task P7b_RankAndDutyTitleFilters_ExcludeUnassignedPeople()
    {
        using var db = PeopleFixture.Create();

        // Every assigned person has a non-empty rank and title; the two unassigned must never appear.
        foreach (var request in new[]
        {
            new GetPeople { Rank = "CPT" }, new GetPeople { Rank = "maj" }, new GetPeople { DutyTitle = "pilot" },
            new GetPeople { Rank = "LTC", DutyTitle = "retired" }
        })
        {
            var names = Names(await Run(db, request));
            Assert.DoesNotContain("Mira Okafor", names);
            Assert.DoesNotContain("Kenji Watanabe", names);
            Assert.NotEmpty(names);
        }
    }

    [Theory]
    [InlineData("name", "x' OR '1'='1")]
    [InlineData("rank", "x' OR '1'='1")]
    [InlineData("dutyTitle", "x' OR '1'='1")]
    [InlineData("name", "'; DROP TABLE Person; --")]
    public async Task P8_InjectionStrings_AreDataNotSql(string filter, string value)
    {
        using var db = PeopleFixture.Create();
        var before = PeopleFixture.Counts(db);
        var request = filter switch
        {
            "name" => new GetPeople { Name = value },
            "rank" => new GetPeople { Rank = value },
            _ => new GetPeople { DutyTitle = value },
        };

        var result = await Run(db, request);

        Assert.Empty(result.People);
        Assert.Equal(before, PeopleFixture.Counts(db));
    }

    [Theory]
    [InlineData("%", new[] { "100% Real" })]
    [InlineData("_", new[] { "Ann_Marie" })]
    [InlineData("\\", new[] { "Back\\Slash" })]
    [InlineData("n_M", new[] { "Ann_Marie" })]
    public async Task P9_LikeWildcardsAreLiteral(string name, string[] expected)
    {
        using var db = PeopleFixture.Create(
            new Person { Name = "100% Real" }, new Person { Name = "Ann_Marie" },
            new Person { Name = "AnnxMarie" }, new Person { Name = "Back\\Slash" });

        Assert.Equal(expected.ToHashSet(), Names(await Run(db, new GetPeople { Name = name })));
    }

    [Fact]
    public async Task P12_NameAndDutyTitleAreTrimmed()
    {
        using var db = PeopleFixture.Create();

        Assert.Equal(["Elena Vasquez"], Names(await Run(db, new GetPeople { Name = " elena " })));
        Assert.Equal(["Desmond Achebe"], Names(await Run(db, new GetPeople { DutyTitle = "retired " })));
    }

    [Fact]
    public async Task P10_WhitespaceOnlyOrNullFilters_MeanNoFilter()
    {
        using var db = PeopleFixture.Create();

        var result = await Run(db, new GetPeople { Name = "   ", Rank = null, DutyTitle = "\t" });

        Assert.Equal(PeopleFixture.AllNames.ToHashSet(), Names(result));
    }

    [Fact]
    public async Task CancelledToken_IsHonoured()
    {
        using var db = PeopleFixture.Create();
        using var context = db.CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new GetPeopleHandler(context).Handle(new GetPeople(), cts.Token));
    }
}
