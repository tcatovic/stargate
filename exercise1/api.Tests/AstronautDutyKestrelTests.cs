using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// The AstronautDuty endpoints on a real Kestrel host: the in-memory TestServer neither enforces [RequestSizeLimit]
/// nor turns an unhandled exception into a 500 response. Temp databases only.
/// </summary>
[Collection(RealKestrel.Collection)]
public sealed class AstronautDutyKestrelTests
{
    private static WebApplicationFactory<Program> StartKestrel(TestDatabase db, string environment, bool migrate)
    {
        if (migrate)
        {
            db.Migrate();
            using var context = db.CreateContext();
            context.People.AddRange(DevelopmentSeedData.People());
            context.SaveChanges();
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:StarbaseApiDatabase", db.ConnectionString);
        });
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task U4_TheRemovedRoute_IsARoutingNotFound_WithAnEmptyBody_OnRealKestrel()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", migrate: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/AstronautDuty/Elena%20Vasquez");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task K2_AnUnexpectedFaultOnGet_InProduction_Is500WithAnEmptyBody()
    {
        using var db = new TestDatabase();
        // Never migrated: no tables, so the handler's first query faults.
        using var factory = StartKestrel(db, "Production", migrate: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/AstronautDuty?name=Secretname%20Probe");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("", body);
    }

    [Fact]
    public async Task K1_TheLocationHeader_IsExactlyEscapedOnRealKestrel()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", migrate: true);
        using var client = factory.CreateClient();

        using var person = await client.PostAsync("/Person", Json("{\"name\":\"Aoife O'Brien Jr.\"}"));
        using var response = await client.PostAsync("/AstronautDuty",
            Json("{\"name\":\"Aoife O'Brien Jr.\",\"rank\":\"CPT\",\"dutyTitle\":\"Mission Specialist\",\"dutyStartDate\":\"2026-03-01\"}"));

        Assert.Equal(HttpStatusCode.Created, person.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/AstronautDuty?name=Aoife%20O%27Brien%20Jr.", response.Headers.Location?.OriginalString);
        using var read = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("Mission Specialist", await read.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task K2b_AnUnexpectedFaultOnPost_InProduction_Is500WithAnEmptyBody()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Production", migrate: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/AstronautDuty",
            Json("{\"name\":\"Secretname Probe\",\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\"}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("", body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task A16_AnUnexpectedFault_InDevelopment_IsNotAJsonEnvelopeCarryingTheExceptionMessage(string method)
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", migrate: true);
        using var client = factory.CreateClient();
        db.Execute("DROP TABLE \"AstronautDuty\"");

        using var response = method == "GET"
            ? await client.GetAsync("/AstronautDuty?name=Elena%20Vasquez")
            : await client.PostAsync("/AstronautDuty",
                Json("{\"name\":\"Kenji Watanabe\",\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\"}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("\"success\"", body);
        Assert.DoesNotContain("\"responseCode\"", body);
    }

    private static string PaddedBody(int bytes)
    {
        const string core = "{\"name\":\"Kenji Watanabe\",\"rank\":\"CPT\",\"dutyTitle\":\"Pilot\",\"dutyStartDate\":\"2024-01-01\"}";
        return core + new string(' ', bytes - core.Length);
    }

    [Fact]
    public async Task K3_ABodyOverTheRequestSizeLimit_Is413_AndWritesNothing_While4096BytesIsAccepted()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", migrate: true);
        using var client = factory.CreateClient();
        var before = db.Dump();

        using var tooLarge = await client.PostAsync("/AstronautDuty", Json(PaddedBody(4097)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(before, db.Dump());

        using var atLimit = await client.PostAsync("/AstronautDuty", Json(PaddedBody(4096)));
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }
}
