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
}
