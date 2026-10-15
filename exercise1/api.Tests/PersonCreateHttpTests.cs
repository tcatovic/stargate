using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// POST /Person over the real HTTP pipeline, with a temp database per test (migrated and seeded here,
/// never starbase.db), identical in Debug and Release.
/// </summary>
public sealed class PersonCreateHttpTests : IDisposable
{
    private const string NameInUseMessage = "A person with this name already exists";

    private readonly TestDatabase _db = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PersonCreateHttpTests()
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

    private sealed record Reply(HttpStatusCode Status, string Body, JsonElement Json, string? Location);

    private async Task<Reply> Send(HttpMethod method, string url, string? body = null, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var location = response.Headers.Location?.OriginalString;
        if (text.Length == 0)
        {
            return new Reply(response.StatusCode, text, default, location);
        }

        using var document = JsonDocument.Parse(text);
        return new Reply(response.StatusCode, text, document.RootElement.Clone(), location);
    }

    private static string NameJson(string? name) => JsonSerializer.Serialize(new { name });

    private Task<Reply> Post(string? name) => Send(HttpMethod.Post, "/Person", NameJson(name));

    private long PersonCount() => _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\"");

    private static void AssertEnvelope409(Reply reply)
    {
        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal(new[] { "id", "message", "responseCode", "success" }, reply.Json.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("id").ValueKind);
        Assert.False(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal(NameInUseMessage, reply.Json.GetProperty("message").GetString());
        Assert.Equal(409, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.Null(reply.Location);
    }

    [Fact]
    public async Task P1_ACreate_Returns201WithTheExactBody_AndTheLocation()
    {
        var reply = await Post("Ada Lovelace");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(new[] { "id", "message", "responseCode", "success" }, reply.Json.EnumerateObject().Select(p => p.Name).Order());
        Assert.True(reply.Json.GetProperty("success").GetBoolean());
        Assert.Equal("Successful", reply.Json.GetProperty("message").GetString());
        Assert.Equal(201, reply.Json.GetProperty("responseCode").GetInt32());
        Assert.Equal(_db.Scalar<long>("SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = 'Ada Lovelace'"), reply.Json.GetProperty("id").GetInt64());
        Assert.Equal("/Person?name=Ada%20Lovelace", reply.Location);
    }

    [Fact]
    public async Task P2_ThePaddedName_IsTrimmed_InTheDatabaseAndInTheLocation()
    {
        var reply = await Post("   Ada Lovelace  ");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(1, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'Ada Lovelace'"));
        Assert.Equal("/Person?name=Ada%20Lovelace", reply.Location);
    }

    [Fact]
    public async Task P3_ReservedCharactersInTheName_AreEscapedInTheLocation_WhichStaysPrintableAscii()
    {
        var reply = await Post("Aoife O'Brien-Smith Jr.");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/Person?name=Aoife%20O%27Brien-Smith%20Jr.", reply.Location);
        Assert.All(reply.Location!, c => Assert.InRange(c, '!', '~'));
    }

    [Theory]
    [InlineData("Mira Okafor")]
    [InlineData("mira okafor")]
    [InlineData("  MIRA OKAFOR ")]
    [InlineData("Mira Okafor\t")]
    public async Task P4_Duplicates_Are409WithTheEnvelope_AndStoreNothing(string name)
    {
        var before = _db.Dump();

        var reply = await Post(name);

        AssertEnvelope409(reply);
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [MemberData(nameof(PersonUpdateHttpTests.InvalidNameBodies), MemberType = typeof(PersonUpdateHttpTests))]
    public async Task P5_InvalidNames_Are400_WithoutEchoingTheValue_AndStoreNothing(string body)
    {
        var before = _db.Dump();

        var reply = await Send(HttpMethod.Post, "/Person", body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(400, reply.Json.GetProperty("status").GetInt32());
        Assert.Contains(reply.Json.GetProperty("errors").EnumerateObject(), e => e.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
        using (var submitted = JsonDocument.Parse(body))
        {
            if (submitted.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString()!.Trim().Length > 0)
            {
                Assert.DoesNotContain(name.GetString()!, reply.Body, StringComparison.Ordinal);
            }
        }

        Assert.Null(reply.Location);
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("\"Tokenabc\"", "Tokenabc")]
    [InlineData("{\"name\":4242424242}", "4242424242")]
    [InlineData("[]", null)]
    [InlineData("{\"name\":", null)]
    [InlineData("{\"name\":\"Tokenxyz\",", "Tokenxyz")]
    public async Task P6_BodyShapeFailures_Are400_WithoutEchoingTheToken_AndStoreNothing(string body, string? token)
    {
        var before = _db.Dump();

        var reply = await Send(HttpMethod.Post, "/Person", body);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(400, reply.Json.GetProperty("status").GetInt32());
        Assert.NotEmpty(reply.Json.GetProperty("errors").EnumerateObject());
        if (token is not null)
        {
            Assert.DoesNotContain(token, reply.Body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"name\":", reply.Body);
        Assert.Equal(before, _db.Dump());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task P7a_ANonJsonContentType_Is415_AndStoresNothing(string contentType)
    {
        var before = _db.Dump();

        var reply = await Send(HttpMethod.Post, "/Person", NameJson("Ada Lovelace"), contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reply.Status);
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task P7b_ABareStringBody_IsNoLongerAccepted()
    {
        var before = _db.Dump();

        var asText = await Send(HttpMethod.Post, "/Person", "\"Ada\"", "text/plain");
        var asJson = await Send(HttpMethod.Post, "/Person", "\"Ada\"");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, asText.Status);
        Assert.Equal(HttpStatusCode.BadRequest, asJson.Status);
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task P7c_NoContentAndNoContentType_Is415()
    {
        var reply = await Send(HttpMethod.Post, "/Person");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reply.Status);
        Assert.Equal(6, PersonCount());
    }

    [Fact]
    public async Task P8_AMultiKilobyteName_Is400_WithoutEchoingIt()
    {
        var before = _db.Dump();

        var reply = await Post(new string('a', 3000));

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.DoesNotContain(new string('a', 50), reply.Body);
        Assert.Equal(before, _db.Dump());
    }

    [Fact]
    public async Task P10_UnknownProperties_AreIgnored_SoAnIdCannotBeChosen()
    {
        var reply = await Send(HttpMethod.Post, "/Person", "{\"name\":\"Kai Extra\",\"id\":999,\"personId\":5,\"role\":\"admin\"}");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var id = reply.Json.GetProperty("id").GetInt32();
        Assert.NotEqual(999, id);
        Assert.NotEqual(5, id);
        Assert.Equal(1, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'Kai Extra'"));
        Assert.Equal(7, PersonCount());
    }

    [Fact]
    public async Task P11_ParallelCreatesOfTheSameName_GiveOne201AndFive409_AndOneRow()
    {
        for (var round = 1; round <= 5; round++)
        {
            string[] names = [$"Zed Unique{round}", $"zed unique{round}", $"ZED UNIQUE{round}", $"Zed unique{round}", $"zed Unique{round}", $" Zed Unique{round} "];

            var replies = await Task.WhenAll(names.Select(Post));

            Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.Created));
            Assert.Equal(5, replies.Count(r => r.Status == HttpStatusCode.Conflict));
            Assert.DoesNotContain(replies, r => (int)r.Status >= 500);
            Assert.Equal(1, _db.Scalar<long>($"SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'zed unique{round}' COLLATE NOCASE"));
            Assert.Equal(6 + round, PersonCount());
        }
    }

    [Fact]
    public async Task P12_SwaggerDocumentsTheCreate()
    {
        var reply = await Send(HttpMethod.Get, "/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var post = reply.Json.GetProperty("paths").GetProperty("/Person").GetProperty("post");
        Assert.Equal("Create a person", post.GetProperty("summary").GetString());
        Assert.Equal(new[] { "201", "400", "409", "415", "500" }, post.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order());
        var schemas = reply.Json.GetProperty("components").GetProperty("schemas");
        var requestRef = post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        Assert.EndsWith("/CreatePersonRequest", requestRef);
        Assert.Equal(
            new[] { "application/json" },
            post.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(c => c.Name).ToArray());
        Assert.True(schemas.GetProperty("CreatePersonRequest").GetProperty("properties").TryGetProperty("name", out _));
        var result = schemas.GetProperty("CreatePersonResult").GetProperty("properties");
        Assert.True(result.TryGetProperty("id", out _));
        Assert.False(result.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task P13_AnApostropheName_IsStoredAndEscapedInTheLocation()
    {
        var reply = await Post("O'Brien");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(1, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'O''Brien'"));
        Assert.Equal("/Person?name=O%27Brien", reply.Location);
    }

    [Fact]
    public async Task P14_ACreate_AddsNoAstronautRows_Rule2()
    {
        var before = _db.Dump("AstronautDetail", "AstronautDuty");

        await Post("Ada Lovelace");

        Assert.Equal(before, _db.Dump("AstronautDetail", "AstronautDuty"));
    }

    [Fact]
    public async Task P15_PostingTheSameNameTwice_Is201ThenAnEnvelope409()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("Ada Lovelace")).Status);

        AssertEnvelope409(await Post("Ada Lovelace"));

        Assert.Equal(1, _db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'Ada Lovelace'"));
    }

    [Fact]
    public async Task P16_AnInvalidNameThatIsAlsoADuplicatePrefix_Is400_Not409()
    {
        var reply = await Post("Mira Okafor<script>");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(6, PersonCount());
    }

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("Aoife O'Brien Jr.")]
    public async Task P17_TheLocation_ReturnsExactlyTheCreatedPerson(string name)
    {
        var created = await Post(name);

        var read = await Send(HttpMethod.Get, created.Location!);

        Assert.Equal(HttpStatusCode.OK, read.Status);
        var people = read.Json.GetProperty("people");
        Assert.Equal(1, people.GetArrayLength());
        var person = people[0];
        Assert.Equal(created.Json.GetProperty("id").GetInt32(), person.GetProperty("personId").GetInt32());
        Assert.Equal(name, person.GetProperty("name").GetString());
        Assert.Equal("", person.GetProperty("currentRank").GetString());
        Assert.Equal("", person.GetProperty("currentDutyTitle").GetString());
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerStartDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, person.GetProperty("careerEndDate").ValueKind);
    }

    [Theory]
    [MemberData(nameof(NonAsciiNames.Data), MemberType = typeof(NonAsciiNames))]
    public async Task X1_NonAsciiNames_Are400_OnPost_WithoutEcho_AndStoreNothing(string value)
    {
        var before = _db.Dump();

        var reply = await Post(value);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.DoesNotContain(value, reply.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Uri.EscapeDataString(value), reply.Body, StringComparison.Ordinal);
        Assert.Null(reply.Location);
        Assert.Equal(before, _db.Dump());
    }
}
