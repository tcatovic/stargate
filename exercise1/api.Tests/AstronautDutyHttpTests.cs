using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StargateAPI.Tests;

/// <summary>
/// GET and POST /AstronautDuty over the real HTTP pipeline against a seeded temp database
/// (Mira 1, Kenji 2, Tomas Lindqvist 3, Priya 4, Elena 5, Desmond 6). Expected values and messages are literals.
/// </summary>
public sealed partial class AstronautDutyHttpTests : IDisposable
{
    private const string NotFound = "Person not found";
    private const string RequiredName = "The name field is required.";

    private readonly DutyApiHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Q(string name) => "/AstronautDuty?name=" + Uri.EscapeDataString(name);

    private static string[] Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] Titles(JsonElement result) =>
        result.GetProperty("astronautDuties").EnumerateArray().Select(d => d.GetProperty("dutyTitle").GetString()!).ToArray();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T00:00:00$")]
    private static partial Regex MidnightDate();

    // ---- GET /AstronautDuty?name= ----

    [Fact]
    public async Task G1_AKnownName_Is200_WithThePersonAndSixFieldDuties_NewestFirst()
    {
        var reply = await _host.Get("/AstronautDuty?name=Elena%20Vasquez");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(["astronautDuties", "message", "person", "responseCode", "success"], Keys(reply.Json));
        Assert.True(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal("Successful", reply.Json.GetProperty("message").GetString());
        Assert.Equal(200, reply.Json.GetProperty("responseCode").GetInt32());
        var person = reply.Json.GetProperty("person");
        Assert.Equal(5, person.GetProperty("personId").GetInt32());
        Assert.Equal("Elena Vasquez", person.GetProperty("name").GetString());
        Assert.Equal("MAJ", person.GetProperty("currentRank").GetString());
        Assert.Equal("Commander", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal("2012-01-09T00:00:00", person.GetProperty("careerStartDate").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerEndDate").ValueKind);
        Assert.Equal(["Commander", "Pilot", "Flight Engineer"], Titles(reply.Json));
        foreach (var duty in reply.Json.GetProperty("astronautDuties").EnumerateArray())
        {
            Assert.Equal(["dutyEndDate", "dutyStartDate", "dutyTitle", "id", "personId", "rank"], Keys(duty));
            Assert.Matches(MidnightDate(), duty.GetProperty("dutyStartDate").GetString()!);
        }
        var duties = reply.Json.GetProperty("astronautDuties");
        Assert.Equal(JsonValueKind.Null, duties[0].GetProperty("dutyEndDate").ValueKind);
        Assert.Equal("2018-12-31T00:00:00", duties[1].GetProperty("dutyEndDate").GetString());
    }

    [Theory]
    [InlineData("elena%20vasquez")]
    [InlineData("%20ELENA%20VASQUEZ%20")]
    [InlineData("ELENA%20VASQUEZ%09")]
    public async Task G2_TheLookupIsTrimmedAndIgnoresAsciiCase(string escaped)
    {
        var reply = await _host.Get("/AstronautDuty?name=" + escaped);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("Elena Vasquez", reply.Json.GetProperty("person").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("ra")]
    [InlineData("Elena")]
    public async Task G3_AFragmentNeverMatches(string fragment)
    {
        var reply = await _host.Get(Q(fragment));

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task G4_ThePersonWithoutAnAssignment_Is200_WithAnEmptyDutyList()
    {
        var reply = await _host.Get("/AstronautDuty?name=Mira%20Okafor");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var person = reply.Json.GetProperty("person");
        Assert.Equal("Mira Okafor", person.GetProperty("name").GetString());
        Assert.Equal("", person.GetProperty("currentRank").GetString());
        Assert.Equal("", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerStartDate").ValueKind);
        Assert.Equal(0, reply.Json.GetProperty("astronautDuties").GetArrayLength());
    }

    [Fact]
    public async Task G5_AnUnknownName_Is404_WithTheFixedBody_AndNoEcho()
    {
        var reply = await _host.Get("/AstronautDuty?name=Nobody");

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal(["astronautDuties", "message", "person", "responseCode", "success"], Keys(reply.Json));
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("person").ValueKind);
        Assert.Equal(0, reply.Json.GetProperty("astronautDuties").GetArrayLength());
        Assert.False(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal(NotFound, reply.Json.GetProperty("message").GetString());
        Assert.Equal(404, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.DoesNotContain("Nobody", reply.Body);
    }

    [Theory]
    [InlineData("/AstronautDuty")]
    [InlineData("/AstronautDuty?name=")]
    [InlineData("/AstronautDuty?name")]
    public async Task G6_AMissingOrEmptyName_Is400_Required(string url)
    {
        var reply = await _host.Get(url);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        ProblemAssert.HasNameErrors(reply.Json, "name", RequiredName);
    }

    [Fact]
    public async Task G7_AWhitespaceOnlyName_Is400_Required()
    {
        var reply = await _host.Get("/AstronautDuty?name=%20%20");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        // Plan item U1: MVC's query binder turns a whitespace-only value into null, so only [Required] fires.
        // contracts/007 says [required, NameRule] here; that statement is wrong for the query parameter (the status is unaffected).
        ProblemAssert.HasNameErrors(reply.Json, "name", RequiredName);
    }

    public static TheoryData<string> InvalidQueryNames() => new()
    {
        new string('a', 101), "a=b", "%", "José", "..", "-", "x' OR '1'='1", "'; DROP TABLE Person;--", "a\"; SELECT 1",
        "Robert'); DROP TABLE AstronautDuty;--",
    };

    [Theory]
    [MemberData(nameof(InvalidQueryNames))]
    public async Task G8_AnInvalidName_Is400_WithTheRuleMessage_AndNoEcho_BeforeAnySqlRuns(string name)
    {
        var before = _host.Db.Dump();

        var reply = await _host.Get(Q(name));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        ProblemAssert.HasNameErrors(reply.Json, "name", ProblemAssert.NameRuleMessage);
        ProblemAssert.NothingDecodedContains(reply.Json, name);
        if (name.Length > 2)
        {
            // Not for "-" and "..": they occur in the generated traceId and type URL. The decoded check above covers them.
            Assert.DoesNotContain(name, reply.Body);
        }
        Assert.Equal(before, _host.Db.Dump());
        Assert.Equal(3, _host.Db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Person', 'AstronautDetail', 'AstronautDuty')"));
    }

    [Fact]
    public async Task G8b_AHundredCharacterNameIsValid_AndJustUnknown()
    {
        var reply = await _host.Get(Q(new string('a', 100)));

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task G9_ARetiredPerson_ListsRetiredFirst_WithTheCareerEnd()
    {
        var reply = await _host.Get("/AstronautDuty?name=Desmond%20Achebe");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(["RETIRED", "Commander", "Pilot"], Titles(reply.Json));
        Assert.Equal("2023-03-31T00:00:00", reply.Json.GetProperty("person").GetProperty("careerEndDate").GetString());
    }

    [Fact]
    public async Task G10_DutiesWithTheSameStartDate_ListTheHigherIdFirst()
    {
        _host.Db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES "
            + "(2, 'CPT', 'First Inserted', '2024-01-01 00:00:00'), (2, 'CPT', 'Second Inserted', '2024-01-01 00:00:00')");

        var reply = await _host.Get("/AstronautDuty?name=Kenji%20Watanabe");

        Assert.Equal(["Second Inserted", "First Inserted"], Titles(reply.Json));
    }

    [Theory]
    [InlineData("Elena%20Vasquez", 5)]
    [InlineData("Desmond%20Achebe", 6)]
    public async Task G11_OnlyThePersonsOwnDutiesAreListed(string escaped, int personId)
    {
        var reply = await _host.Get("/AstronautDuty?name=" + escaped);

        Assert.Equal(personId, reply.Json.GetProperty("person").GetProperty("personId").GetInt32());
        Assert.Equal(3, reply.Json.GetProperty("astronautDuties").GetArrayLength());
        Assert.All(reply.Json.GetProperty("astronautDuties").EnumerateArray(), d => Assert.Equal(personId, d.GetProperty("personId").GetInt32()));
    }

    [Theory]
    [InlineData("/AstronautDuty/Elena%20Vasquez")]
    [InlineData("/AstronautDuty/Nobody")]
    public async Task G12_TheRemovedRoute_IsARoutingNotFound_WithAnEmptyBody(string url)
    {
        var reply = await _host.Get(url);

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal("", reply.Body);
    }

    [Fact]
    public async Task G13_UnknownQueryParametersAreIgnored()
    {
        var reply = await _host.Get("/AstronautDuty?name=Elena%20Vasquez&rank=X");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("Elena Vasquez", reply.Json.GetProperty("person").GetProperty("name").GetString());
    }

    // ---- Read path of the injection (S2, S3) ----

    [Fact]
    public async Task S2_TheHostilePersonsOwnDutiesComeBack_NeverAnotherPersons()
    {
        const string name = "a' OR Id IS 3 OR 'a";
        Assert.Equal(HttpStatusCode.Created, (await _host.PostPerson(name)).Status);
        _host.Db.Execute("INSERT INTO \"AstronautDuty\" (\"PersonId\", \"Rank\", \"DutyTitle\", \"DutyStartDate\") VALUES (7, 'CPT', 'Injected Role', '2026-03-01 00:00:00')");

        var reply = await _host.Get("/AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(7, reply.Json.GetProperty("person").GetProperty("personId").GetInt32());
        Assert.Equal(name, reply.Json.GetProperty("person").GetProperty("name").GetString());
        Assert.Equal(["Injected Role"], Titles(reply.Json));
        Assert.All(reply.Json.GetProperty("astronautDuties").EnumerateArray(), d => Assert.Equal(7, d.GetProperty("personId").GetInt32()));
        Assert.DoesNotContain("Tomas", reply.Body);
        Assert.DoesNotContain("Pilot", reply.Body);
    }

    [Theory]
    [InlineData("b' OR Id IS 3 OR 'b")]
    [InlineData("b' OR 1 OR 'b")]
    public async Task S3_APayloadShapedUnknownName_Is404_NeverAWiderResult(string name)
    {
        var reply = await _host.Get(Q(name));

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("person").ValueKind);
        Assert.Equal(0, reply.Json.GetProperty("astronautDuties").GetArrayLength());
        foreach (var leak in new[] { "Tomas", "Mira", name })
        {
            Assert.DoesNotContain(leak, reply.Body);
        }
    }

    public static TheoryData<string> RuleFailingPayloads() => new()
    {
        "x' OR '1'='1", "'; DROP TABLE Person;--", "a\"; SELECT 1", "Robert'); DROP TABLE AstronautDuty;--",
    };

    [Theory]
    [MemberData(nameof(RuleFailingPayloads))]
    public async Task S4_ARuleFailingPayload_Is400OnPostPersonAndOnGet_WithNoEchoAndNothingWritten(string payload)
    {
        var before = _host.Db.Dump();

        var person = await _host.PostPerson(payload);
        var get = await _host.Get(Q(payload));

        foreach (var reply in new[] { person, get })
        {
            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            ProblemAssert.HasNameErrors(reply.Json, "name", ProblemAssert.NameRuleMessage);
            ProblemAssert.NothingDecodedContains(reply.Json, payload);
        }
        Assert.Equal(before, _host.Db.Dump());
        Assert.Equal(3, _host.Db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Person', 'AstronautDetail', 'AstronautDuty')"));
    }
}
