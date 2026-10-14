using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StargateAPI.Tests;

public sealed class PersonHttpProductionTests
{
    [Fact]
    public async Task DatabaseFault_InProduction_Is500WithoutInternalDetails()
    {
        using var db = new TestDatabase(); // never migrated: no tables
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:StarbaseApiDatabase", db.ConnectionString);
        });
        // The in-memory TestServer rethrows unhandled exceptions to the caller; a real Kestrel host turns them into the 500 a client sees.
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/Person");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("no such table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteException", body);
        Assert.DoesNotContain("Person", body);
    }
}
