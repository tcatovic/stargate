using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// POST /Person on a real Kestrel host: the in-memory TestServer neither enforces [RequestSizeLimit] nor turns an
/// unhandled exception into a 500 response. Temp databases only.
/// </summary>
[Collection(RealKestrel.Collection)]
public sealed class PersonCreateKestrelTests
{
    private static WebApplicationFactory<Program> StartKestrel(TestDatabase db, string environment, bool seed)
    {
        db.Migrate();
        if (seed)
        {
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
    public async Task K1_TheLocationHeader_IsExactlyEscapedOnRealKestrel()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", seed: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/Person", Json("{\"name\":\"Aoife O'Brien Jr.\"}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/Person?name=Aoife%20O%27Brien%20Jr.", response.Headers.Location?.OriginalString);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\" WHERE \"Name\" = 'Aoife O''Brien Jr.'"));
    }

    [Fact]
    public async Task Q1k_TheRemovedPersonRoute_Is405WithAllowPutAndAnEmptyBody_OnRealKestrel()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", seed: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Person/Elena%20Vasquez");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(new[] { "PUT" }, response.Content.Headers.Allow.ToArray());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task K2_AnUnexpectedFault_InProduction_Is500WithoutInternalDetails()
    {
        using var db = new TestDatabase();
        // Never migrated: no tables, so the handler's first query faults.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:StarbaseApiDatabase", db.ConnectionString);
        });
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/Person", Json("{\"name\":\"Secretname Probe\"}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        foreach (var leak in new[] { "no such table", "SqliteException", "Person", "Secretname", "Exception" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task K3_ABodyOverTheRequestSizeLimit_Is413_AndStoresNothing()
    {
        using var db = new TestDatabase();
        using var factory = StartKestrel(db, "Development", seed: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/Person", Json("{\"name\":\"" + new string('a', 5000) + "\"}"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(6, db.Scalar<long>("SELECT COUNT(*) FROM \"Person\""));
    }
}
