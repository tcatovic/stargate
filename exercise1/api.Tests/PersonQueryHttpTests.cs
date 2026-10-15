using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// GET /Person as a single bound query object (task 008): binding, prefix pinning, trimming, matching, the 400 shape
/// and the removed GET /Person/{name} route. Real HTTP pipeline over a migrated, seeded temp database per test.
/// Expected values are written out as literals, not read from production constants.
/// </summary>
public sealed class PersonQueryHttpTests : IDisposable
{
    private static readonly string[] All6 =
        ["Desmond Achebe", "Elena Vasquez", "Kenji Watanabe", "Mira Okafor", "Priya Raman", "Tomas Lindqvist"];

    private const string SwaggerPattern = @"^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z";

    private readonly TestDatabase _db = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PersonQueryHttpTests()
    {
        _db.Migrate();
        using (var context = _db.CreateContext())
        {
            context.People.AddRange(DevelopmentSeedData.People());
            context.SaveChanges();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:StarbaseApiDatabase", _db.ConnectionString);
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _db.Dispose();
    }

    private async Task<(HttpStatusCode Status, string Body, JsonElement Json)> Get(string url)
    {
        using var response = await _client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return (response.StatusCode, body, document.RootElement.Clone());
    }

    private static string[] Names(JsonElement json) =>
        json.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray();

    private static void AssertEnvelope200(JsonElement json)
    {
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Successful", json.GetProperty("message").GetString());
        Assert.Equal(200, json.GetProperty("responseCode").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("people").ValueKind);
    }

    private async Task AssertPeople(string query, params string[] expected)
    {
        var (status, body, json) = await Get("/Person" + query);

        Assert.True(HttpStatusCode.OK == status, $"{query} -> {(int)status} {body}");
        AssertEnvelope200(json);
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), Names(json));
    }

    /// <summary>400 whose errors object has exactly the listed property names (ordinal), each with exactly the name-rule message.</summary>
    private async Task AssertRejected(string query, string[] keys, params string[] submitted)
    {
        var before = _db.Dump();

        var (status, body, json) = await Get("/Person" + query);

        Assert.True(HttpStatusCode.BadRequest == status, $"{query} -> {(int)status} {body}");
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        var errors = json.GetProperty("errors").EnumerateObject().ToList();
        Assert.Equal(keys.Order(StringComparer.Ordinal).ToArray(), errors.Select(e => e.Name).Order(StringComparer.Ordinal).ToArray());
        foreach (var key in keys)
        {
            ProblemAssert.HasOnlyNameError(json, key, ProblemAssert.NameRuleMessage);
        }
        ProblemAssert.NothingDecodedContains(json, submitted);
        Assert.Equal(before, _db.Dump());
    }

    // ---- Q1 removed route ----

    [Theory]
    [InlineData("/Person/Elena%20Vasquez")]
    [InlineData("/Person/elena%20vasquez")]
    [InlineData("/Person/Nobody")]
    [InlineData("/Person/1")]
    [InlineData("/Person/%20")]
    [InlineData("/Person/a%2Fb")]
    [InlineData("/Person/x'%20OR%20'1'%3D'1")]
    public async Task Q1_TheRemovedRoute_Is405WithAllowPut_AndAnEmptyBody(string path)
    {
        var before = _db.Dump();

        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(new[] { "PUT" }, response.Content.Headers.Allow.ToArray());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task Q1h_HeadOnTheRemovedRoute_Is405WithAllowPut()
    {
        using var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/Person/Elena%20Vasquez"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(new[] { "PUT" }, response.Content.Headers.Allow.ToArray());
    }

    [Fact]
    public async Task Q1p_PutWithANonNumericId_IsStill400_SoNoRouteConstraintWasAdded()
    {
        using var content = new StringContent("{\"name\":\"Zed\"}", System.Text.Encoding.UTF8, "application/json");
        var before = _db.Dump();

        using var response = await _client.PutAsync("/Person/abc", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _db.Dump());
    }

    // ---- Q2 parameter-name casing ----

    [Theory]
    [InlineData("?name=Elena%20Vasquez", "Elena Vasquez")]
    [InlineData("?Name=Elena%20Vasquez", "Elena Vasquez")]
    [InlineData("?NAME=elena%20vasquez", "Elena Vasquez")]
    [InlineData("?rank=LTC", "Desmond Achebe")]
    [InlineData("?RANK=ltc", "Desmond Achebe")]
    [InlineData("?dutyTitle=retired", "Desmond Achebe")]
    [InlineData("?dutytitle=RETIRED", "Desmond Achebe")]
    [InlineData("?DUTYTITLE=Retired", "Desmond Achebe")]
    public Task Q2_ParameterNames_AreMatchedIgnoringCase(string query, string expected) => AssertPeople(query, expected);

    // ---- Q3 filters ----

    [Fact]
    public Task Q3a_NoFilter_ReturnsEveryone() => AssertPeople("", All6);

    [Fact]
    public async Task Q3b_AnUnassignedPerson_IsReturnedByNameAlone_WithEmptyRankAndTitle()
    {
        await AssertPeople("?name=Mira%20Okafor", "Mira Okafor");

        var (_, _, json) = await Get("/Person?name=Mira%20Okafor");
        var mira = Assert.Single(json.GetProperty("people").EnumerateArray());
        Assert.Equal(
            new[] { "careerEndDate", "careerStartDate", "currentDutyTitle", "currentRank", "name", "personId" },
            mira.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, mira.GetProperty("personId").GetInt32());
        Assert.Equal(string.Empty, mira.GetProperty("currentRank").GetString());
        Assert.Equal(string.Empty, mira.GetProperty("currentDutyTitle").GetString());
        Assert.Equal(JsonValueKind.Null, mira.GetProperty("careerStartDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, mira.GetProperty("careerEndDate").ValueKind);
    }

    [Fact]
    public async Task Q3c_AnUnassignedPerson_NeverMatchesARankFilter() => await AssertPeople("?name=Mira%20Okafor&rank=MAJ");

    [Fact]
    public async Task Q3d_AnUnassignedPerson_NeverMatchesADutyTitleFilter() => await AssertPeople("?name=Mira%20Okafor&dutyTitle=Pilot");

    [Fact]
    public Task Q3e_RankAlone_IsCaseInsensitive() => AssertPeople("?rank=maj", "Priya Raman", "Elena Vasquez");

    [Fact]
    public Task Q3f_DutyTitleAlone_IsCaseInsensitive() => AssertPeople("?dutyTitle=mission%20specialist", "Priya Raman");

    [Fact]
    public async Task Q3g_AnUnknownName_Is200WithAnEmptyList_AndExactlyTheEnvelope()
    {
        var (status, body, _) = await Get("/Person?name=Nobody");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("{\"people\":[],\"success\":true,\"message\":\"Successful\",\"responseCode\":200}", body);
    }

    [Fact]
    public Task Q3h_AllThreeFilters_MatchOnePerson() => AssertPeople("?name=Elena%20Vasquez&rank=MAJ&dutyTitle=Commander", "Elena Vasquez");

    [Fact]
    public Task Q3i_FiltersAreAnded() => AssertPeople("?name=Elena%20Vasquez&rank=MAJ&dutyTitle=Pilot");

    [Fact]
    public Task Q3j_RankAndDutyTitle_AreAnded() => AssertPeople("?rank=MAJ&dutyTitle=commander", "Elena Vasquez");

    [Theory]
    [InlineData("?name=Elena")]
    [InlineData("?name=Vasquez")]
    [InlineData("?name=lena%20Vasquez")]
    [InlineData("?name=Elena%20Vasques")]
    [InlineData("?name=ra")]
    public Task Q3k_ANameFragment_NeverMatches(string query) => AssertPeople(query);

    [Theory]
    [InlineData("?rank=MA")]
    [InlineData("?dutyTitle=Command")]
    public Task Q3l_ARankOrTitleFragment_NeverMatches(string query) => AssertPeople(query);

    [Fact]
    public async Task Q3m_EveryItem_HasExactlyTheSixDocumentedMembers()
    {
        var (_, _, json) = await Get("/Person");

        Assert.Equal(6, json.GetProperty("people").GetArrayLength());
        foreach (var item in json.GetProperty("people").EnumerateArray())
        {
            Assert.Equal(
                new[] { "careerEndDate", "careerStartDate", "currentDutyTitle", "currentRank", "name", "personId" },
                item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        }
    }

    // ---- Q4 repeated parameters: the first value wins ----

    [Fact]
    public Task Q4a_RepeatedName_FirstWins() => AssertPeople("?name=Elena%20Vasquez&name=Nobody", "Elena Vasquez");

    [Fact]
    public Task Q4b_RepeatedName_FirstWins_EvenWhenItMatchesNothing() => AssertPeople("?name=Nobody&name=Elena%20Vasquez");

    [Fact]
    public Task Q4c_RepeatedRank_FirstWins() => AssertPeople("?rank=CPT&rank=MAJ", "Tomas Lindqvist");

    [Fact]
    public Task Q4d_OnlyTheFirstValue_IsBoundAndValidated() => AssertPeople("?name=Elena%20Vasquez&name=%25", "Elena Vasquez");

    // ---- Q5 unknown and prefix-like parameters never bypass binding or validation ----

    [Fact]
    public Task Q5a_AQueryKey_DoesNotDropTheNameFilter() => AssertPeople("?query=x&name=Elena%20Vasquez", "Elena Vasquez");

    [Fact]
    public Task Q5b_AQueryKey_DoesNotSkipValidation() => AssertRejected("?query=x&name=%25", ["name"], "%");

    [Fact]
    public Task Q5c_AQueryKeyInAnyCase_DoesNotDropTheRankFilter() => AssertPeople("?Query=x&rank=CPT", "Tomas Lindqvist");

    [Fact]
    public Task Q5d_QueryDotName_IsNotAnAlias() => AssertPeople("?query.name=Elena%20Vasquez", All6);

    [Theory]
    [InlineData("?foo=bar&name=Elena%20Vasquez")]
    [InlineData("?people=x&name=Elena%20Vasquez")]
    public Task Q5e_UnknownParameters_AreIgnored(string query) => AssertPeople(query, "Elena Vasquez");

    [Fact]
    public Task Q5f_AKeyWithoutAValue_MeansNoFilter() => AssertPeople("?name", All6);

    // ---- Q6 trimming is the write rule ----

    [Theory]
    [InlineData("?name=%09Elena%20Vasquez", new[] { "Elena Vasquez" })]
    [InlineData("?name=Elena%20Vasquez%0A", new[] { "Elena Vasquez" })]
    [InlineData("?name=%0D%0AElena%20Vasquez%0B%0C", new[] { "Elena Vasquez" })]
    [InlineData("?dutyTitle=%09retired%20", new[] { "Desmond Achebe" })]
    [InlineData("?rank=%20maj%09", new[] { "Priya Raman", "Elena Vasquez" })]
    public Task Q6_EdgeAsciiWhitespace_IsTrimmedBeforeMatching(string query, string[] expected) => AssertPeople(query, expected);

    [Fact]
    public Task Q6f_100CharactersAfterTrimming_IsAccepted() => AssertPeople("?name=%20" + new string('a', 100));

    [Fact]
    public Task Q6g_100CharactersFollowedByTabs_IsAccepted() => AssertPeople("?name=" + new string('a', 100) + "%09%09%09");

    [Fact]
    public Task Q6h_101CharactersAfterTrimming_Is400() =>
        AssertRejected("?name=%20" + new string('a', 101) + "%20", ["name"], new string('a', 101));

    [Fact]
    public Task Q6i_ATabInsideAValue_Is400() => AssertRejected("?dutyTitle=a%09b", ["dutyTitle"], "a\tb");

    [Fact]
    public Task Q6j_ANoBreakSpaceAtTheEdge_IsNotTrimmed_AndIs400() => AssertRejected("?name=%C2%A0Elena%20Vasquez", ["name"], " Elena Vasquez");

    [Theory]
    [InlineData("?name=%C2%A0")]
    [InlineData("?rank=%E3%80%80")]
    [InlineData("?name=%09&rank=%0A&dutyTitle=%20")]
    public Task Q6k_WhitespaceOnlyValues_MeanNoFilter(string query) => AssertPeople(query, All6);

    // ---- Q7 400 cases ----

    [Theory]
    [InlineData("?name=%25", "name", "%")]
    [InlineData("?name=_", "name", "_")]
    [InlineData("?rank=a%2Fb", "rank", "a/b")]
    [InlineData("?dutyTitle=%3Cscript%3E", "dutyTitle", "<script>")]
    [InlineData("?name=..", "name", "..")]
    [InlineData("?name=%C3%89mile", "name", "Émile")]
    [InlineData("?name=x'%20OR%20'1'%3D'1", "name", "x' OR '1'='1")]
    public Task Q7_InvalidValues_Are400_WithTheExactKey_AndNoEcho(string query, string key, string value) =>
        AssertRejected(query, [key], value);

    [Fact]
    public Task Q7_101Characters_IsOneNameRuleMessage() => AssertRejected("?name=" + new string('a', 101), ["name"], new string('a', 101));

    [Fact]
    public Task Q7_101PercentSigns_IsExactlyOneMessage() =>
        AssertRejected("?name=" + string.Concat(Enumerable.Repeat("%25", 101)), ["name"], new string('%', 101));

    [Fact]
    public Task Q7_SeveralInvalidKeys_AreAllReported() =>
        AssertRejected("?name=%25&rank=%25&dutyTitle=%25", ["name", "rank", "dutyTitle"], "%");

    [Fact]
    public Task Q7_OnlyTheInvalidKey_IsReported() => AssertRejected("?name=Elena%20Vasquez&rank=%25", ["rank"], "%");

    [Fact]
    public async Task Q7_TheProblemBody_HasTheDocumentedShape()
    {
        using var response = await _client.GetAsync("/Person?name=%25");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new[] { "errors", "status", "title", "traceId", "type" },
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("One or more validation errors occurred.", document.RootElement.GetProperty("title").GetString());
    }

    // ---- Q8 hostile values are data ----

    [Theory]
    [InlineData("?name=x'%20OR%20'a'%20LIKE%20'a")]
    [InlineData("?name=%09x'%20OR%20'a'%20LIKE%20'a%0A")]
    [InlineData("?rank=MAJ'--")]
    [InlineData("?dutyTitle=Commander'%20OR%20'1")]
    [InlineData("?name=Robert'.%20DROP%20TABLE%20Person")]
    public async Task Q8_HostileValues_AreDataAndNeverWidenTheResult(string query)
    {
        var before = _db.Dump();

        var (status, body, json) = await Get("/Person" + query);

        Assert.True(status is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{query} -> {(int)status} {body}");
        if (status == HttpStatusCode.OK)
        {
            Assert.Empty(Names(json));
        }
        await AssertPeople("", All6);
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task Q8_A4000CharacterValue_Is400_NotA500()
    {
        await AssertRejected("?name=" + new string('a', 4000), ["name"], new string('a', 4000));
    }

    // ---- Q9 Swagger ----

    [Fact]
    public async Task Q9a_SwaggerListsExactlyTheThreeParameters_WithTheirLimits()
    {
        var (status, _, json) = await Get("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, status);

        var operation = json.GetProperty("paths").GetProperty("/Person").GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToList();
        Assert.Equal(new[] { "dutyTitle", "name", "rank" }, parameters.Select(p => p.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray());
        var descriptions = new Dictionary<string, string>
        {
            ["name"] = "Case-insensitive exact match on the person's full name. Empty or whitespace-only means no filter.",
            ["rank"] = "Case-insensitive exact match on the current rank (e.g. MAJ). Excludes people with no assignment.",
            ["dutyTitle"] = "Case-insensitive exact match on the current duty title (e.g. Commander, RETIRED). Excludes people with no assignment.",
        };
        foreach (var parameter in parameters)
        {
            var name = parameter.GetProperty("name").GetString()!;
            Assert.Equal("query", parameter.GetProperty("in").GetString());
            Assert.False(parameter.TryGetProperty("required", out var required) && required.GetBoolean());
            Assert.Equal(descriptions[name], parameter.GetProperty("description").GetString());
            var schema = parameter.GetProperty("schema");
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.Equal(100, schema.GetProperty("maxLength").GetInt32());
            Assert.Equal(SwaggerPattern, schema.GetProperty("pattern").GetString());
            Assert.False(schema.TryGetProperty("minLength", out _));
            Assert.DoesNotContain('.', name);
        }
        Assert.Equal(new[] { "200", "400", "500" }, operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Q9b_SwaggerNoLongerDescribesTheRemovedRoute()
    {
        var (_, _, json) = await Get("/swagger/v1/swagger.json");

        var paths = json.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/Person/{name}", out _));
        Assert.Equal(new[] { "put" }, paths.GetProperty("/Person/{personId}").EnumerateObject().Select(o => o.Name).ToArray());
        Assert.DoesNotContain(json.GetProperty("components").GetProperty("schemas").EnumerateObject(), o => o.Name.StartsWith("GetPersonBy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Q9c_TheDutiesNameParameter_GainsTheSameLimits_AndStaysRequired()
    {
        var (_, _, json) = await Get("/swagger/v1/swagger.json");

        var parameters = json.GetProperty("paths").GetProperty("/AstronautDuty").GetProperty("get").GetProperty("parameters").EnumerateArray().ToList();
        var name = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "name");
        Assert.True(name.GetProperty("required").GetBoolean());
        Assert.Equal(100, name.GetProperty("schema").GetProperty("maxLength").GetInt32());
        Assert.Equal(SwaggerPattern, name.GetProperty("schema").GetProperty("pattern").GetString());
    }

    [Fact]
    public async Task Q9d_RequestBodySchemas_AreNotGivenParameterLimits()
    {
        var (_, _, json) = await Get("/swagger/v1/swagger.json");

        var schema = json.GetProperty("components").GetProperty("schemas").GetProperty("CreatePersonRequest");
        var name = schema.GetProperty("properties").GetProperty("name");
        Assert.False(name.TryGetProperty("maxLength", out _));
        Assert.False(name.TryGetProperty("pattern", out _));
    }
}
