using StargateAPI.Business.Data;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

public class GetPersonByNameQueryTests
{
    private static async Task<GetPersonByNameResult> Run(TestDatabase db, string name)
    {
        using var context = db.CreateContext();
        return await new GetPersonByNameHandler(context).Handle(new GetPersonByName { Name = name }, CancellationToken.None);
    }

    private static void AssertNotFound(GetPersonByNameResult result)
    {
        Assert.Equal(404, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal("Person not found", result.Message);
        Assert.Null(result.Person);
    }

    [Fact]
    public async Task N1_FindsAnAssignedPerson()
    {
        using var db = PeopleFixture.Create();

        var result = await Run(db, "Elena Vasquez");

        Assert.True(result.Success);
        Assert.Equal("Successful", result.Message);
        Assert.Equal(200, result.ResponseCode);
        Assert.NotNull(result.Person);
        Assert.Equal("MAJ", result.Person.CurrentRank);
        Assert.Equal("Commander", result.Person.CurrentDutyTitle);
        Assert.Equal(new DateTime(2012, 1, 9), result.Person.CareerStartDate);
    }

    [Fact]
    public async Task N2_IsCaseInsensitive()
    {
        using var db = PeopleFixture.Create();
        var result = await Run(db, "elena VASQUEZ");
        Assert.Equal("Elena Vasquez", result.Person?.Name);
    }

    [Fact]
    public async Task N3_Unknown_Is404_AndDoesNotEchoTheInput()
    {
        using var db = PeopleFixture.Create();
        var result = await Run(db, "Nobody");
        AssertNotFound(result);
        Assert.DoesNotContain("Nobody", result.Message);
    }

    [Theory]
    [InlineData("Mira")]
    [InlineData("%")]
    [InlineData("_ira Okafor")]
    public async Task N4_NoContainsAndNoWildcards(string name)
    {
        using var db = PeopleFixture.Create();
        AssertNotFound(await Run(db, name));
    }

    [Theory]
    [InlineData("x' OR '1'='1")]
    [InlineData("'; DROP TABLE Person; --")]
    public async Task N5_InjectionStrings_Are404_AndTablesIntact(string name)
    {
        using var db = PeopleFixture.Create();
        var before = PeopleFixture.Counts(db);

        AssertNotFound(await Run(db, name));

        Assert.Equal(before, PeopleFixture.Counts(db));
    }

    [Theory]
    [InlineData("Aoife O'Brien")]
    [InlineData("aoife o'brien")]
    public async Task N6_ApostropheNamesAreSafe(string name)
    {
        using var db = PeopleFixture.Create(new Person { Name = "Aoife O'Brien" });
        var result = await Run(db, name);
        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Aoife O'Brien", result.Person?.Name);
    }

    [Fact]
    public async Task N7_IsTrimmed()
    {
        using var db = PeopleFixture.Create();
        Assert.Equal("Mira Okafor", (await Run(db, "  Mira Okafor  ")).Person?.Name);
    }

    [Fact]
    public async Task N8_CaseDuplicates_ResolveToExactCaseThenLowestId()
    {
        using var db = PeopleFixture.Create(new Person { Name = "MIRA OKAFOR" });
        var mira = db.Scalar<long>("SELECT Id FROM Person WHERE Name = 'Mira Okafor'");
        var upper = db.Scalar<long>("SELECT Id FROM Person WHERE Name = 'MIRA OKAFOR'");
        Assert.True(upper > mira);

        Assert.Equal((int)upper, (await Run(db, "MIRA OKAFOR")).Person?.PersonId);
        Assert.Equal((int)mira, (await Run(db, "Mira Okafor")).Person?.PersonId);
        Assert.Equal((int)mira, (await Run(db, "mira okafor")).Person?.PersonId);
    }

    [Fact]
    public async Task N9_UnassignedPersonKeepsEmptyStringsAndNullDates()
    {
        using var db = PeopleFixture.Create();

        var person = (await Run(db, "Kenji Watanabe")).Person;

        Assert.NotNull(person);
        Assert.Equal("", person.CurrentRank);
        Assert.Equal("", person.CurrentDutyTitle);
        Assert.Null(person.CareerStartDate);
        Assert.Null(person.CareerEndDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task N10_BlankName_Is404(string name)
    {
        using var db = PeopleFixture.Create();
        AssertNotFound(await Run(db, name));
    }
}
