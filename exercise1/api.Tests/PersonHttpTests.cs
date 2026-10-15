using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// Real HTTP pipeline (model validation, routing, URL decoding, Swagger) over a temp database per test.
/// The database is migrated and seeded here, not by the app's Debug-only initializer, so the tests behave
/// identically in Debug and Release and never touch starbase.db.
/// </summary>
public sealed class PersonHttpTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PersonHttpTests()
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
        // Clone so the element outlives the document.
        using var document = JsonDocument.Parse(body);
        return (response.StatusCode, body, document.RootElement.Clone());
    }

    private async Task AssertBadRequest(string url, string errorKey, string? secret = null)
    {
        var (status, body, json) = await Get(url);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.True(json.GetProperty("errors").TryGetProperty(errorKey, out _), body);
        if (secret is not null)
        {
            Assert.DoesNotContain(secret, body);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("rank")]
    [InlineData("dutyTitle")]
    public async Task H1_TooLongFilter_Is400_WithOnlyTheSharedNameRuleMessage(string parameter)
    {
        var (status, _, json) = await Get($"/Person?{parameter}={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var errors = json.GetProperty("errors").EnumerateObject().ToList();
        Assert.Equal(parameter, Assert.Single(errors).Name); // ordinal: the wire name, not Name/Rank/DutyTitle
        ProblemAssert.HasOnlyNameError(json, parameter, ProblemAssert.NameRuleMessage);
    }

    [Theory]
    [InlineData("/Person?name=%25", "name", "%")]
    [InlineData("/Person?name=_", "name", "_")]
    [InlineData("/Person?rank=a%2Fb", "rank", "a/b")]
    [InlineData("/Person?dutyTitle=%3Cscript%3E", "dutyTitle", "<script>")]
    public Task H2_DisallowedCharacters_Are400_AndTheValueIsNotEchoed(string url, string key, string value) =>
        AssertBadRequest(url, key, value);

    // Edge control whitespace is trimmed before validation, exactly like the write endpoints, so it is accepted.
    [Theory]
    [InlineData("/Person?name=abc%0A")]
    [InlineData("/Person?name=%0Aabc")]
    [InlineData("/Person?rank=abc%0A")]
    [InlineData("/Person?dutyTitle=%0Aabc")]
    public async Task H2b_EdgeNewlines_AreTrimmed_LikeWrites(string url)
    {
        var (status, _, json) = await Get(url);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(200, json.GetProperty("responseCode").GetInt32());
        Assert.Equal(0, json.GetProperty("people").GetArrayLength());
    }

    // Whitespace-only values never reach validation over HTTP: MVC binds them to null (ConvertEmptyStringToNull), the same
    // as the handler's own "whitespace means no filter" (GetPeopleQueryTests.P10). So a lone tab is 200 with everyone,
    // exactly like %20 in H7. A tab inside a longer value is a disallowed character and is rejected (H2b covers newlines).
    [Fact]
    public async Task H2c_TabOnlyFilter_BindsToNull_AndMeansNoFilter()
    {
        var (status, _, json) = await Get("/Person?dutyTitle=%09");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(6, json.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public Task H2d_TabInsideAValue_Is400() => AssertBadRequest("/Person?dutyTitle=a%09b", "dutyTitle");

    [Fact]
    public async Task H3_QuoteEqualsInjectionString_FailsTheCharacterRule()
    {
        foreach (var url in new[] { "/Person?name=x'%20OR%20'1'%3D'1" })
        {
            var (status, body, _) = await Get(url);
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.DoesNotContain("1'='1", body);
            Assert.DoesNotContain("SELECT", body);
            Assert.DoesNotContain("LIKE", body);
        }
    }

    [Fact]
    public async Task H4_ApostropheInjectionAttempt_ThatPassesTheRule_IsSafeData()
    {
        var (status, _, json) = await Get("/Person?name=x'%20OR%20'a'%20LIKE%20'a");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, json.GetProperty("people").GetArrayLength());

        var (_, _, all) = await Get("/Person");
        Assert.Equal(6, all.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public async Task H5b_ApostropheNameRoundTrips()
    {
        using (var context = _db.CreateContext())
        {
            context.People.Add(new Person { Name = "Aoife O'Brien" });
            context.SaveChanges();
        }

        var (status, _, json) = await Get("/Person?name=aoife%20o'brien");

        Assert.Equal(HttpStatusCode.OK, status);
        var people = json.GetProperty("people");
        Assert.Equal(1, people.GetArrayLength());
        Assert.Equal("Aoife O'Brien", people[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task H7_EmptyAndWhitespaceFilters_MeanNoFilter()
    {
        var (status, _, json) = await Get("/Person?name=&rank=%20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(6, json.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public async Task H10_FilterOverHttp_ReturnsTheMatchingPerson()
    {
        var (_, _, json) = await Get("/Person?rank=maj&dutyTitle=COMMANDER");

        var people = json.GetProperty("people");
        Assert.Equal(1, people.GetArrayLength());
        Assert.Equal("Elena Vasquez", people[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task H9_SwaggerDocumentsParametersAndResponses()
    {
        var (status, _, json) = await Get("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, status);

        var list = json.GetProperty("paths").GetProperty("/Person").GetProperty("get");
        var parameters = list.GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        Assert.Equal(new[] { "dutyTitle", "name", "rank" }, parameters.Keys.Order());
        foreach (var parameter in parameters.Values)
        {
            Assert.Equal("query", parameter.GetProperty("in").GetString());
            var schema = parameter.GetProperty("schema");
            Assert.Equal(100, schema.GetProperty("maxLength").GetInt32());
            Assert.True(schema.TryGetProperty("pattern", out _));
        }
        Assert.Equal(new[] { "200", "400", "500" }, ResponseCodes(list));

        var paths = json.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/Person/{name}", out _));
        Assert.Equal(new[] { "put" }, paths.GetProperty("/Person/{personId}").EnumerateObject().Select(o => o.Name).ToArray());
        Assert.DoesNotContain(json.GetProperty("components").GetProperty("schemas").EnumerateObject(), o => o.Name.StartsWith("GetPersonBy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ra")]
    [InlineData("elena")]
    [InlineData("Okafor")]
    [InlineData("Mira%20Okafo")]
    public async Task F1_APartialName_ReturnsAnEmptyList(string name)
    {
        var (status, _, json) = await Get("/Person?name=" + name);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("people").ValueKind);
        Assert.Equal(0, json.GetProperty("people").GetArrayLength());
    }

    [Theory]
    [InlineData("Elena%20Vasquez")]
    [InlineData("ELENA%20VASQUEZ")]
    [InlineData("%20%20elena%20vasquez%20")]
    public async Task F2_TheFullNameInAnyCaseWithPadding_FindsExactlyThatPerson(string name)
    {
        var (status, _, json) = await Get("/Person?name=" + name);

        Assert.Equal(HttpStatusCode.OK, status);
        var people = json.GetProperty("people");
        Assert.Equal(1, people.GetArrayLength());
        Assert.Equal("Elena Vasquez", people[0].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("priya%20raman&rank=MAJ", new[] { "Priya Raman" })]
    [InlineData("priya%20raman&rank=CPT", new string[0])]
    [InlineData("mira%20okafor&dutyTitle=Pilot", new string[0])]
    public async Task F3_TheNameFilterCombinesWithTheOthers(string query, string[] expected)
    {
        var (_, _, json) = await Get("/Person?name=" + query);

        Assert.Equal(expected, json.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public async Task F4_SwaggerDescribesTheNameFilterAsExact_AndEmitsThePatternVerbatim()
    {
        var (_, _, json) = await Get("/swagger/v1/swagger.json");

        var parameters = json.GetProperty("paths").GetProperty("/Person").GetProperty("get").GetProperty("parameters")
            .EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        var description = parameters["name"].GetProperty("description").GetString()!;
        Assert.Contains("exact", description);
        Assert.DoesNotContain("contains", description);
        foreach (var parameter in parameters.Values)
        {
            Assert.Equal(@"^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z", parameter.GetProperty("schema").GetProperty("pattern").GetString());
        }
    }

    [Theory]
    [InlineData("name", "..")]
    [InlineData("name", "'")]
    [InlineData("rank", ".")]
    [InlineData("dutyTitle", "-")]
    public Task F6_APunctuationOnlyFilter_Is400(string parameter, string value) =>
        AssertBadRequest($"/Person?{parameter}={Uri.EscapeDataString(value)}", parameter);

    [Fact]
    public async Task F7_ALetterAndPunctuationFilter_IsAccepted()
    {
        var (status, _, json) = await Get("/Person?name=a.");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, json.GetProperty("people").GetArrayLength());
    }

    private static string[] ResponseCodes(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();
}
