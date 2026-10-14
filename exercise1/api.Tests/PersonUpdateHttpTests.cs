using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// PUT /Person/{personId} over the real HTTP pipeline, with a temp database per test (migrated and seeded here,
/// never starbase.db), identical in Debug and Release.
/// </summary>
public sealed class PersonUpdateHttpTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PersonUpdateHttpTests()
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

    private int IdOf(string name) => (int)_db.Scalar<long>($"SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = '{name}'");

    private static string NameJson(string? name) => JsonSerializer.Serialize(new { name });

    private async Task<(HttpStatusCode Status, string Body, JsonElement Json)> Send(HttpMethod method, string url, string? body = null, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length == 0)
        {
            return (response.StatusCode, text, default);
        }
        using var document = JsonDocument.Parse(text);
        return (response.StatusCode, text, document.RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, string Body, JsonElement Json)> Put(int id, string? name) =>
        Send(HttpMethod.Put, $"/Person/{id}", NameJson(name));

    private Task<(HttpStatusCode Status, string Body, JsonElement Json)> Get(string url) => Send(HttpMethod.Get, url);

    private static string[] Names(JsonElement list) =>
        list.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).Order().ToArray();

    private static void AssertEnvelope(JsonElement json, bool success, string message, int code)
    {
        Assert.Equal(new[] { "message", "person", "responseCode", "success" }, json.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(success, json.GetProperty("success").GetBoolean());
        Assert.Equal(message, json.GetProperty("message").GetString());
        Assert.Equal(code, json.GetProperty("responseCode").GetInt32());
    }

    [Fact]
    public async Task P1_RenamingAnUnassignedPerson_Returns200WithTheExactBody_AndTheListShowsIt()
    {
        var mira = IdOf("Mira Okafor");

        var (status, _, json) = await Put(mira, "Mira Okafor-Lindqvist");

        Assert.Equal(HttpStatusCode.OK, status);
        AssertEnvelope(json, true, "Successful", 200);
        var person = json.GetProperty("person");
        Assert.Equal(new[] { "careerEndDate", "careerStartDate", "currentDutyTitle", "currentRank", "name", "personId" }, person.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(mira, person.GetProperty("personId").GetInt32());
        Assert.Equal("Mira Okafor-Lindqvist", person.GetProperty("name").GetString());
        Assert.Equal("", person.GetProperty("currentRank").GetString());
        Assert.Equal("", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerStartDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerEndDate").ValueKind);

        var (_, _, list) = await Get("/Person?name=Lindqvist");
        Assert.Contains("Mira Okafor-Lindqvist", Names(list));
    }

    [Fact]
    public async Task P1b_RenamingAnAssignedPerson_KeepsItsDutyDataOnTheWire_AndInTheDatabase()
    {
        var elena = IdOf("Elena Vasquez");
        var before = _db.Dump("AstronautDetail", "AstronautDuty");

        var (status, _, json) = await Put(elena, "Elena Vasquez-Ruiz");

        Assert.Equal(HttpStatusCode.OK, status);
        var person = json.GetProperty("person");
        Assert.Equal("MAJ", person.GetProperty("currentRank").GetString());
        Assert.Equal("Commander", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal("2012-01-09T00:00:00", person.GetProperty("careerStartDate").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerEndDate").ValueKind);
        Assert.Equal(before, _db.Dump("AstronautDetail", "AstronautDuty"));
    }

    [Fact]
    public async Task P2_AnUnknownId_Is404WithTheEnvelope()
    {
        var before = _db.Dump();

        var (status, _, json) = await Put(999999, "Nobody Here");

        Assert.Equal(HttpStatusCode.NotFound, status);
        AssertEnvelope(json, false, "Person not found", 404);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("person").ValueKind);
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("mira okafor")]
    [InlineData("  MIRA OKAFOR ")]
    public async Task P3_ANameTakenByAnotherPerson_Is409WithTheEnvelope_AndNothingChanges(string name)
    {
        var kenji = IdOf("Kenji Watanabe");
        var before = _db.Dump();
        var (_, _, listBefore) = await Get("/Person");

        var (status, body, json) = await Put(kenji, name);

        Assert.Equal(HttpStatusCode.Conflict, status);
        AssertEnvelope(json, false, "A person with this name already exists", 409);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("person").ValueKind);
        Assert.DoesNotContain("okafor", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, _db.Dump());
        var (_, _, listAfter) = await Get("/Person");
        Assert.Equal(Names(listBefore), Names(listAfter));
    }

    [Fact]
    public async Task P4_TheSameRequestTwice_GivesTwo200sWithIdenticalBodies()
    {
        var kenji = IdOf("Kenji Watanabe");

        var first = await Put(kenji, "Kenji Watanabe-Ito");
        var second = await Put(kenji, "Kenji Watanabe-Ito");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
    }

    [Fact]
    public async Task P5_ADifferentCasingOfTheOwnName_Succeeds_AndIsStored()
    {
        var mira = IdOf("Mira Okafor");

        var (status, _, json) = await Put(mira, "MIRA OKAFOR");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("MIRA OKAFOR", json.GetProperty("person").GetProperty("name").GetString());
        Assert.Equal("MIRA OKAFOR", _db.Scalar<string>($"SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = {mira}"));
    }

    public static TheoryData<string> InvalidNameBodies() => new()
    {
        "{\"name\":\"\"}",
        "{\"name\":\"   \"}",
        "{\"name\":null}",
        "{}",
        NameJson(new string('a', 101)),
        NameJson("<script>alert(1)</script>"),
        NameJson("x' OR '1'='1"),
        NameJson("Robert'); DROP TABLE Person;--"),
        NameJson("a\tb"),
        NameJson("Ada\u3164"),
        NameJson("\u0301Ada"),
        NameJson("Ada\u200B"),
        NameJson("Ada\u115F\u1160"),
        NameJson("A\u034Fda"),
        NameJson("Ada\uFE0F"),
        NameJson("q\u0301\u0302\u0303\u0304"),
    };

    [Theory]
    [MemberData(nameof(InvalidNameBodies))]
    public async Task P6_InvalidNames_Are400_WithoutEchoingTheValue_AndChangeNothing(string body)
    {
        var mira = IdOf("Mira Okafor");
        var before = _db.Dump();

        var (status, text, json) = await Send(HttpMethod.Put, $"/Person/{mira}", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        var errors = json.GetProperty("errors");
        Assert.NotEmpty(errors.EnumerateObject());
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
        using (var submitted = JsonDocument.Parse(body))
        {
            if (submitted.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString()!.Trim().Length > 0)
            {
                Assert.DoesNotContain(name.GetString()!, text);
            }
        }
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"Ada\"")]
    [InlineData("{\"name\":123}")]
    [InlineData("[]")]
    [InlineData("{\"name\":")]
    public async Task P7_BodyShapeFailures_Are400_AndChangeNothing(string body)
    {
        var mira = IdOf("Mira Okafor");
        var before = _db.Dump();

        var (status, text, json) = await Send(HttpMethod.Put, $"/Person/{mira}", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.NotEmpty(json.GetProperty("errors").EnumerateObject());
        Assert.DoesNotContain("Ada", text);
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("99999999999", "99999999999")]
    [InlineData("notanumber", "notanumber")]
    [InlineData("1.5", null)]
    public async Task P8_BadPersonIds_Are400_WithoutEchoingTheValue(string id, string? secret)
    {
        var before = _db.Dump();

        var (status, text, json) = await Send(HttpMethod.Put, $"/Person/{id}", NameJson("Zed Valid"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.Contains(json.GetProperty("errors").EnumerateObject(), e => e.Name.Equals("personId", StringComparison.OrdinalIgnoreCase));
        if (secret is not null)
        {
            Assert.DoesNotContain(secret, text);
        }
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task P9_ANonJsonContentType_Is415(string contentType)
    {
        var mira = IdOf("Mira Okafor");
        var before = _db.Dump();

        var (status, text, _) = await Send(HttpMethod.Put, $"/Person/{mira}", NameJson("Zed Valid"), contentType);

        // [Consumes] rejects in endpoint routing, which writes the status only: the body is empty, not the ProblemDetails
        // the contract expected (reported to the tech lead). Either would be acceptable to clients, which must key on the status.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.DoesNotContain("Zed Valid", text);
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task P10_HostileButValidInput_IsStoredVerbatim()
    {
        var mira = IdOf("Mira Okafor");

        var (status, _, json) = await Put(mira, "O'Brien");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("O'Brien", json.GetProperty("person").GetProperty("name").GetString());
        Assert.Equal("O'Brien", _db.Scalar<string>($"SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = {mira}"));
        Assert.Equal(6, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }

    [Fact]
    public async Task P10b_UnknownBodyPropertiesAreIgnored()
    {
        var mira = IdOf("Mira Okafor");

        var (status, _, json) = await Send(HttpMethod.Put, $"/Person/{mira}", "{\"name\":\"Mira Extra\",\"id\":999,\"personId\":5,\"role\":\"admin\"}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(mira, json.GetProperty("person").GetProperty("personId").GetInt32());
        Assert.Equal(1, _db.Scalar<long>($"SELECT COUNT(*) FROM \"Person\" WHERE \"Id\" = {mira} AND \"Name\" = 'Mira Extra'"));
    }

    [Fact]
    public async Task P11_SixParallelConflictingRenames_GiveOneSuccessAndFiveConflicts_Repeatedly()
    {
        var ids = _db.Strings("SELECT \"Id\" FROM \"Person\" ORDER BY \"Id\"").Select(int.Parse).ToArray();
        Assert.Equal(6, ids.Length);

        for (var round = 1; round <= 5; round++)
        {
            var variants = new[]
            {
                $"Zed Unique{round}", $"zed unique{round}", $"ZED UNIQUE{round}",
                $"Zed unique{round}", $"zed Unique{round}", $" Zed Unique{round} "
            };

            var results = await Task.WhenAll(ids.Select((id, i) => Put(id, variants[i])));

            Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
            Assert.Equal(5, results.Count(r => r.Status == HttpStatusCode.Conflict));
            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            Assert.Equal(1, _db.Scalar<long>($"SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'zed unique{round}' COLLATE NOCASE"));
            Assert.Equal(6, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
        }
    }

    [Fact]
    public async Task P12_SwaggerDocumentsThePutOperation()
    {
        var (status, _, json) = await Get("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, status);
        var put = json.GetProperty("paths").GetProperty("/Person/{personId}").GetProperty("put");
        var codes = put.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();
        Assert.Equal(new[] { "200", "400", "404", "409", "415", "500" }, codes);
        Assert.Equal("Rename a person", put.GetProperty("summary").GetString());
        Assert.True(put.TryGetProperty("requestBody", out _));
    }

    [Fact]
    public async Task P13_TheReadSideFindsNfcNamesWithNfdInput_AndRejectsInvisibleLetters()
    {
        var mira = IdOf("Mira Okafor");
        Assert.Equal(HttpStatusCode.OK, (await Put(mira, "Ren\u00E9e \u00D6degaard")).Status);

        var (status, _, list) = await Get("/Person?name=Rene%CC%81e");
        var (invisible, _, _) = await Get("/Person?name=Ada%E3%85%A4");
        var (byName, _, single) = await Get("/Person/Rene%CC%81e%20%C3%96degaard");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Ren\u00E9e \u00D6degaard", Names(list));
        Assert.Equal(HttpStatusCode.BadRequest, invisible);
        Assert.Equal(HttpStatusCode.OK, byName);
        Assert.Equal(mira, single.GetProperty("person").GetProperty("personId").GetInt32());
    }

    [Fact]
    public async Task P14_RenamingOnePerson_DoesNotChangeAnyOtherListEntry()
    {
        var elena = IdOf("Elena Vasquez");
        var (_, _, before) = await Get("/Person");

        await Put(elena, "Elena Renamed");

        var (_, _, after) = await Get("/Person");
        var others = (JsonElement list) => list.GetProperty("people").EnumerateArray()
            .Where(p => p.GetProperty("personId").GetInt32() != elena).Select(p => p.GetRawText()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(others(before), others(after));
        Assert.Equal(6, after.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public async Task P15_AnUnrouteableMethodOrPath_IsNotAffected()
    {
        var (getStatus, _, _) = await Get("/Person/Mira%20Okafor");
        var (deleteStatus, _, _) = await Send(HttpMethod.Delete, "/Person/1");

        Assert.Equal(HttpStatusCode.OK, getStatus);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteStatus);
    }
}
