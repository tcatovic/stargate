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
    public Task H1_TooLongFilter_Is400(string parameter) =>
        AssertBadRequest($"/Person?{parameter}={new string('a', 101)}", parameter);

    [Theory]
    [InlineData("/Person?name=%25", "name", "%")]
    [InlineData("/Person?name=_", "name", "_")]
    [InlineData("/Person?rank=a%2Fb", "rank", "a/b")]
    [InlineData("/Person?dutyTitle=%3Cscript%3E", "dutyTitle", "<script>")]
    public Task H2_DisallowedCharacters_Are400_AndTheValueIsNotEchoed(string url, string key, string value) =>
        AssertBadRequest(url, key, value);

    [Fact]
    public async Task H3_QuoteEqualsInjectionString_FailsTheCharacterRule()
    {
        await AssertBadRequest("/Person?name=x'%20OR%20'1'%3D'1", "name", "OR");
        await AssertBadRequest("/Person/x'%20OR%20'1'%3D'1", "name", "OR");
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

    [Theory]
    [InlineData("/Person/Mira%20Okafor")]
    [InlineData("/Person/mira%20okafor")]
    public async Task H5_FindsByNameIgnoringCase_AndDecodesSpaces(string url)
    {
        var (status, _, json) = await Get(url);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Mira Okafor", json.GetProperty("person").GetProperty("name").GetString());
        Assert.Equal(200, json.GetProperty("responseCode").GetInt32());
    }

    [Fact]
    public async Task H5b_ApostropheNameRoundTrips()
    {
        using (var context = _db.CreateContext())
        {
            context.People.Add(new Person { Name = "Aoife O'Brien" });
            context.SaveChanges();
        }

        var (status, _, json) = await Get("/Person/aoife%20o'brien");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Aoife O'Brien", json.GetProperty("person").GetProperty("name").GetString());
    }

    [Fact]
    public async Task H6_UnknownName_Is404WithTheEnvelope()
    {
        var (status, _, json) = await Get("/Person/Nobody");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(404, json.GetProperty("responseCode").GetInt32());
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("person").ValueKind);
    }

    [Fact]
    public async Task H7_EmptyAndWhitespaceFilters_MeanNoFilter()
    {
        var (status, _, json) = await Get("/Person?name=&rank=%20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(6, json.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public async Task H8_BlankPathName_Is400() => await AssertBadRequest("/Person/%20", "name");

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

        var single = json.GetProperty("paths").GetProperty("/Person/{name}").GetProperty("get");
        Assert.Equal(new[] { "200", "400", "404", "500" }, ResponseCodes(single));
    }

    private static string[] ResponseCodes(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();
}
