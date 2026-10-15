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
    public async Task S4_ARuleFailingPayload_Is400OnPostPersonPostDutyAndGet_WithNoEchoAndNothingWritten(string payload)
    {
        var before = _host.Db.Dump();

        var person = await _host.PostPerson(payload);
        var get = await _host.Get(Q(payload));
        var duty = await _host.PostDuty(payload, "CPT", "Pilot", "2026-03-01");

        foreach (var reply in new[] { person, get, duty })
        {
            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            ProblemAssert.HasNameErrors(reply.Json, "name", ProblemAssert.NameRuleMessage);
            ProblemAssert.NothingDecodedContains(reply.Json, payload);
        }
        Assert.Equal(before, _host.Db.Dump());
        Assert.Equal(3, _host.Db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Person', 'AstronautDetail', 'AstronautDuty')"));
    }

    // ---- POST /AstronautDuty ----

    private const string Base = "{\"name\":\"Kenji Watanabe\",\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\"}";
    private const string Dup = "A duty with this title and start date already exists for this person";
    private const string DateMsg = "Use a date between 1900-01-01 and 2100-12-31.";

    private Task<DutyApiHost.Reply> Post(string body, string? contentType = "application/json") =>
        _host.Send(HttpMethod.Post, "/AstronautDuty", body, contentType);

    private static string Body(string name = "Kenji Watanabe", string rank = "CPT", string title = "Pilot", string? start = "\"2024-01-01\"") =>
        "{\"name\":" + JsonSerializer.Serialize(name) + ",\"rank\":" + JsonSerializer.Serialize(rank)
        + ",\"dutyTitle\":" + JsonSerializer.Serialize(title) + (start is null ? "" : ",\"dutyStartDate\":" + start) + "}";

    private string DutyRowsOf(int personId) => string.Join("\n", _host.Db.Strings(
        $"SELECT \"Rank\" || '|' || \"DutyTitle\" || '|' || \"DutyStartDate\" || '|' || COALESCE(\"DutyEndDate\", '<null>') FROM \"AstronautDuty\" WHERE \"PersonId\" = {personId} ORDER BY \"DutyStartDate\", \"Id\""));

    [Fact]
    public async Task A1_ACreate_Is201_WithTheExactBody_AndALocationThatReturnsTheNewDuty()
    {
        var reply = await Post(Base);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(["id", "message", "responseCode", "success"], Keys(reply.Json));
        var id = reply.Json.GetProperty("id").GetInt32();
        Assert.True(id >= 1);
        Assert.True(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal("Successful", reply.Json.GetProperty("message").GetString());
        Assert.Equal(201, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.Equal("/AstronautDuty?name=Kenji%20Watanabe", reply.Location);

        var read = await _host.Get(reply.Location!);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        var duties = read.Json.GetProperty("astronautDuties");
        Assert.Equal(1, duties.GetArrayLength());
        Assert.Equal(id, duties[0].GetProperty("id").GetInt32());
        var person = read.Json.GetProperty("person");
        Assert.Equal("CPT", person.GetProperty("currentRank").GetString());
        Assert.Equal("Pilot", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal("2024-01-01T00:00:00", person.GetProperty("careerStartDate").GetString());
    }

    [Fact]
    public async Task A2_APaddedAndDifferentlyCasedName_ResolvesToThePerson_AndTheLocationUsesTheStoredName()
    {
        var reply = await Post(Body(name: "  kenji WATANABE "));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=Kenji%20Watanabe", reply.Location);
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DutyRowsOf(2));
    }

    [Fact]
    public async Task A3_AnUnknownPerson_Is404_WithTheFixedBody_NoLocation_AndNothingWritten()
    {
        var before = _host.Db.Dump();

        var reply = await Post(Body(name: "Nobody"));

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        Assert.Equal(["id", "message", "responseCode", "success"], Keys(reply.Json));
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("id").ValueKind);
        Assert.False(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal(NotFound, reply.Json.GetProperty("message").GetString());
        Assert.Equal(404, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.Null(reply.Location);
        Assert.DoesNotContain("Nobody", reply.Body);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Fact]
    public async Task A4_ADuplicate_Is409_WithTheFixedBody_AndTheSecondRequestWritesNothing()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post(Base)).Status);
        var before = _host.Db.Dump();

        var reply = await Post(Base);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal(["id", "message", "responseCode", "success"], Keys(reply.Json));
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("id").ValueKind);
        Assert.False(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal(Dup, reply.Json.GetProperty("message").GetString());
        Assert.Equal(409, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.Null(reply.Location);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Fact]
    public async Task A5_ADuplicateIgnoresTheTimePartAndTheTitleCase()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post(Base)).Status);
        var before = _host.Db.Dump();

        var reply = await Post(Body(title: "PILOT", start: "\"2024-01-01T15:30:00\""));

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal(Dup, reply.Json.GetProperty("message").GetString());
        Assert.Equal(before, _host.Db.Dump());
    }

    [Fact]
    public async Task A6_TheSameTitleAndDateForAnotherPerson_IsNotADuplicate()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post(Base)).Status);

        var reply = await Post(Body(name: "Mira Okafor"));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=Mira%20Okafor", reply.Location);
    }

    [Fact]
    public async Task A7_TheSameDateWithADifferentTitle_IsAccepted_PinningTodaysBehaviourForTask009()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post(Base)).Status);

        var reply = await Post(Body(title: "Commander"));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    public static TheoryData<string, string, string?, string[]> InvalidTextFields()
    {
        var data = new TheoryData<string, string, string?, string[]>();
        foreach (var field in new[] { "name", "rank", "dutyTitle" })
        {
            var display = char.ToUpperInvariant(field[0]) + field[1..];
            var required = $"The {display} field is required.";
            data.Add(field, "omitted", null, [required]);
            data.Add(field, "null", "null", [required]);
            data.Add(field, "empty", "\"\"", [required, ProblemAssert.NameRuleMessage]);
            data.Add(field, "blank", "\"   \"", [required, ProblemAssert.NameRuleMessage]);
            data.Add(field, "quote payload", "\"x' OR '1'='1\"", [ProblemAssert.NameRuleMessage]);
            data.Add(field, "semicolon", "\"a;b\"", [ProblemAssert.NameRuleMessage]);
            data.Add(field, "too long", "\"" + new string('a', 101) + "\"", [ProblemAssert.NameRuleMessage]);
            data.Add(field, "non-ascii", "\"Jos\\u00E9\"", [ProblemAssert.NameRuleMessage]);
            data.Add(field, "dots only", "\"..\"", [ProblemAssert.NameRuleMessage]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidTextFields))]
    public async Task A8_AnInvalidNameRankOrTitle_Is400_WithTheExactMessages_NoEcho_AndNothingWritten(string field, string kind, string? json, string[] messages)
    {
        _ = kind;
        var members = new Dictionary<string, string>
        {
            ["name"] = "\"Kenji Watanabe\"", ["rank"] = "\"CPT\"", ["dutyTitle"] = "\"Pilot\"", ["dutyStartDate"] = "\"2024-01-01\"",
        };
        if (json is null) members.Remove(field); else members[field] = json;
        var body = "{" + string.Join(",", members.Select(m => $"\"{m.Key}\":{m.Value}")) + "}";
        var before = _host.Db.Dump();

        var reply = await Post(body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        ProblemAssert.HasNameErrors(reply.Json, field, messages);
        Assert.Single(reply.Json.GetProperty("errors").EnumerateObject());
        if (json is not null && json.Length > 2)
        {
            var submitted = JsonSerializer.Deserialize<string?>(json);
            if (!string.IsNullOrWhiteSpace(submitted))
            {
                ProblemAssert.NothingDecodedContains(reply.Json, submitted);
                Assert.DoesNotContain(submitted, reply.Body);
            }
        }
        Assert.Null(reply.Location);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Theory]
    [InlineData(null, "The DutyStartDate field is required.")]
    [InlineData("null", "The DutyStartDate field is required.")]
    [InlineData("\"0001-01-01\"", DateMsg)]
    [InlineData("\"1899-12-31\"", DateMsg)]
    [InlineData("\"2101-01-01\"", DateMsg)]
    public async Task A9_ARequiredAndBoundedStartDate_Is400_WithTheExactMessage_AndNothingWritten(string? raw, string message)
    {
        var before = _host.Db.Dump();

        var reply = await Post(Body(start: raw));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        ProblemAssert.HasNameErrors(reply.Json, "dutyStartDate", message);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Theory]
    [InlineData("\"1900-01-01\"", "1900-01-01 00:00:00")]
    [InlineData("\"2100-12-31\"", "2100-12-31 00:00:00")]
    [InlineData("\"2100-12-31T23:59:59\"", "2100-12-31 00:00:00")]
    public async Task A9b_TheDateBoundsAreInclusive(string raw, string stored)
    {
        var reply = await Post(Body(name: "Mira Okafor", start: raw));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"CPT|Pilot|{stored}|<null>", DutyRowsOf(1));
    }

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"2024-13-01\"")]
    [InlineData("12345")]
    [InlineData("{}")]
    public async Task A9c_AnUnparseableStartDate_Is400_WithGenericMessagesOnly(string raw)
    {
        var before = _host.Db.Dump();

        var reply = await Post(Body(start: raw));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Json.GetProperty("errors").EnumerateObject());
        ProblemAssert.NoParserInternals(reply.Json);
        Assert.DoesNotContain("not-a-date", reply.Body);
        Assert.DoesNotContain("2024-13-01", reply.Body);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"Kenji\"")]
    [InlineData("{\"name\":4242424242,\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\"}")]
    [InlineData("{\"name\":\"Kenji Watanabe\",\"rank\":\"CPT\"")]
    [InlineData("{\"name\":")]
    [InlineData("")]
    public async Task A10_MalformedBodies_Are400_WithoutParserInternals_AndNothingWritten(string body)
    {
        var before = _host.Db.Dump();

        var reply = await Post(body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotEmpty(reply.Json.GetProperty("errors").EnumerateObject());
        ProblemAssert.NoParserInternals(reply.Json);
        Assert.DoesNotContain("4242424242", reply.Body);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task A11_ANonJsonContentType_Is415_WithAnEmptyBody_AndNothingWritten(string contentType)
    {
        var before = _host.Db.Dump();

        var reply = await Post(Base, contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reply.Status);
        Assert.Equal("", reply.Body);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Fact]
    public async Task A11b_NoContentAndNoContentType_Is415()
    {
        var before = _host.Db.Dump();

        var reply = await _host.Send(HttpMethod.Post, "/AstronautDuty");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reply.Status);
        Assert.Equal(before, _host.Db.Dump());
    }

    [Fact]
    public async Task A11c_AJsonContentTypeWithACharset_IsAccepted()
    {
        var reply = await Post(Base, "application/json; charset=utf-8");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Fact]
    public async Task A12_UnknownProperties_AreIgnored_SoAnIdOrPersonCannotBeChosen()
    {
        var others = _host.Db.DumpExcept(2);

        var reply = await Post("{\"name\":\"Kenji Watanabe\",\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\",\"id\":999,\"personId\":3}");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.NotEqual(999, reply.Json.GetProperty("id").GetInt32());
        Assert.Equal("CPT|Pilot|2024-01-01 00:00:00|<null>", DutyRowsOf(2));
        Assert.Equal(0, _host.Db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"Id\" = 999"));
        Assert.Equal(others, _host.Db.DumpExcept(2));
    }

    [Fact]
    public async Task A13_TheOrderOfChecks_Is400_Then404_Then409()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post(Base)).Status);

        var invalidRankUnknownPerson = await Post(Body(name: "Nobody", rank: "a;b"));
        var duplicateWithInvalidTitle = await Post(Body(title: "a;b"));
        var unknownPersonDuplicateShape = await Post(Body(name: "Nobody"));

        Assert.Equal(HttpStatusCode.BadRequest, invalidRankUnknownPerson.Status);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateWithInvalidTitle.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknownPersonDuplicateShape.Status);
    }

    [Fact]
    public async Task A14_TheResponseCodeInTheBody_EqualsTheHttpStatus()
    {
        var created = await Post(Base);
        var conflict = await Post(Base);
        var notFound = await Post(Body(name: "Nobody"));

        foreach (var reply in new[] { created, conflict, notFound })
        {
            Assert.Equal((int)reply.Status, reply.Json.GetProperty("responseCode").GetInt32());
        }
    }

    [Fact]
    public async Task A15_RankAndTitleAreStoredTrimmed_InTheDutyAndTheDetail()
    {
        var reply = await Post(Body(name: "Elena Vasquez", rank: " MAJ ", title: "\tRETIRED ", start: "\"2025-01-01\""));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("MAJ|RETIRED|2025-01-01 00:00:00|<null>", DutyRowsOf(5).Split('\n')[^1]);
        Assert.Equal("RETIRED", _host.Db.Scalar<string>("SELECT \"CurrentDutyTitle\" FROM \"AstronautDetail\" WHERE \"PersonId\" = 5"));
        // A padded RETIRED counts as retired (the career ends the day before).
        Assert.Equal("2024-12-31 00:00:00", _host.Db.Scalar<string>("SELECT \"CareerEndDate\" FROM \"AstronautDetail\" WHERE \"PersonId\" = 5"));
    }

    [Fact]
    public async Task A17_ASuccessfulCreate_ChangesNoOtherPersonsRows()
    {
        var others = _host.Db.DumpExcept(3);

        var reply = await Post(Body(name: "Tomas Lindqvist", rank: "MAJ", title: "Commander", start: "\"2024-03-01\""));

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("CPT|Pilot|2021-06-14 00:00:00|2024-02-29 00:00:00\nMAJ|Commander|2024-03-01 00:00:00|<null>", DutyRowsOf(3));
        Assert.Equal(others, _host.Db.DumpExcept(3));
    }

    [Fact]
    public async Task A20_SwaggerDocumentsBothActions_AndNotTheRemovedRoute()
    {
        var reply = await _host.Get("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var paths = reply.Json.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/AstronautDuty/{name}", out _));
        var item = paths.GetProperty("/AstronautDuty");

        var get = item.GetProperty("get");
        var name = Assert.Single(get.GetProperty("parameters").EnumerateArray());
        Assert.Equal("name", name.GetProperty("name").GetString());
        Assert.Equal("query", name.GetProperty("in").GetString());
        Assert.True(name.GetProperty("required").GetBoolean());
        Assert.Equal(["200", "400", "404", "500"], get.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray());

        var post = item.GetProperty("post");
        Assert.Equal("Add a duty to a person", post.GetProperty("summary").GetString());
        Assert.Equal(["201", "400", "404", "409", "415", "500"], post.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray());
        Assert.Equal(["application/json"], post.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(c => c.Name).ToArray());
        var schemas = reply.Json.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["dutyStartDate", "dutyTitle", "name", "rank"], schemas.GetProperty("CreateAstronautDutyRequest").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(["dutyStartDate", "dutyTitle", "name", "rank"], schemas.GetProperty("CreateAstronautDutyRequest").GetProperty("required").EnumerateArray().Select(p => p.GetString()!).Order().ToArray());
        Assert.False(schemas.GetProperty("CreateAstronautDutyResult").GetProperty("properties").TryGetProperty("name", out _));
        Assert.Equal(
            ["dutyEndDate", "dutyStartDate", "dutyTitle", "id", "personId", "rank"],
            schemas.GetProperty("AstronautDutyItem").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray());
    }
}
