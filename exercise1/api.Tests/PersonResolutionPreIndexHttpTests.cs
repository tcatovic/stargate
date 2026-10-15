using System.Net;

namespace StargateAPI.Tests;

/// <summary>
/// On a database that predates AddUniquePersonNameIndex two people can have names that differ only in case.
/// Every lookup by name must pick the same one: an exact-case match first, then the lowest Id.
/// (Production environment: Development would migrate the schema forward, and that migration refuses such names.)
/// </summary>
public sealed class PersonResolutionPreIndexHttpTests : IDisposable
{
    private readonly DutyApiHost _host = new(seed: false, migrateTo: TestDatabase.MakeCareerStartDateRequired, environment: "Production");

    public PersonResolutionPreIndexHttpTests()
    {
        _host.Db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (4, 'bob'), (9, 'Bob')");
        _host.Db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES "
            + "(4, 'CPT', 'Lowercase Bob Duty', '2020-01-01 00:00:00'), (9, 'MAJ', 'Uppercase Bob Duty', '2021-01-01 00:00:00')");
    }

    public void Dispose() => _host.Dispose();

    private long DutiesOf(int personId, string title) =>
        _host.Db.Scalar<long>($"SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"PersonId\" = {personId} AND \"DutyTitle\" = '{title}'");

    [Fact]
    public async Task PostWithTheExactCaseName_WritesToThatPerson_NotTheLowestId()
    {
        Assert.Equal("Bob", _host.Db.Scalar<string>("SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = 9"));

        var reply = await _host.PostDuty("Bob", "CPT", "New Duty", "2024-01-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=Bob", reply.Location);
        Assert.Equal(1, DutiesOf(9, "New Duty"));
        Assert.Equal(0, DutiesOf(4, "New Duty"));
    }

    [Fact]
    public async Task PostWithTheLowerCaseName_WritesToThePersonWithThatExactName()
    {
        var reply = await _host.PostDuty("bob", "CPT", "New Duty", "2024-01-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(1, DutiesOf(4, "New Duty"));
        Assert.Equal(0, DutiesOf(9, "New Duty"));
    }

    [Fact]
    public async Task PostWithANameThatMatchesNeitherExactly_WritesToTheLowestId()
    {
        var reply = await _host.PostDuty("BOB", "CPT", "New Duty", "2024-01-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(1, DutiesOf(4, "New Duty"));
        Assert.Equal(0, DutiesOf(9, "New Duty"));
    }

    [Theory]
    [InlineData("Bob", 9, "Uppercase Bob Duty")]
    [InlineData("bob", 4, "Lowercase Bob Duty")]
    [InlineData("BOB", 4, "Lowercase Bob Duty")]
    public async Task GetDuties_AndGetPerson_ResolveTheSamePerson(string name, int expectedId, string expectedTitle)
    {
        var duties = await _host.Get("/AstronautDuty?name=" + name);
        var person = await _host.Get("/Person/" + name);

        Assert.Equal(HttpStatusCode.OK, duties.Status);
        Assert.Equal(expectedId, duties.Json.GetProperty("person").GetProperty("personId").GetInt32());
        Assert.Equal(expectedTitle, Assert.Single(duties.Json.GetProperty("astronautDuties").EnumerateArray()).GetProperty("dutyTitle").GetString());
        Assert.Equal(HttpStatusCode.OK, person.Status);
        Assert.Equal(expectedId, person.Json.GetProperty("person").GetProperty("personId").GetInt32());
    }
}
