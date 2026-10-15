using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>The ASCII-only name rule is enforced identically on every endpoint that takes a name (temp database per test).</summary>
public sealed class NameRuleHttpTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NameRuleHttpTests()
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

    private async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string url, string? json = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string NameJson(string name) => System.Text.Json.JsonSerializer.Serialize(new { name });

    [Theory]
    [MemberData(nameof(NonAsciiNames.Data), MemberType = typeof(NonAsciiNames))]
    public async Task X1_NonAsciiNames_Are400_OnEveryEndpoint_WithoutEcho_AndChangeNothing(string value)
    {
        var mira = _db.Scalar<long>("SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = 'Mira Okafor'");
        var before = _db.Dump();
        var escaped = Uri.EscapeDataString(value);

        var requests = new[]
        {
            ("name", await Send(HttpMethod.Get, "/Person?name=" + escaped)),
            ("name", await Send(HttpMethod.Get, "/Person/" + escaped)),
            ("Name", await Send(HttpMethod.Put, $"/Person/{mira}", NameJson(value))),
        };

        foreach (var (key, (status, body)) in requests)
        {
            Assert.Equal(HttpStatusCode.BadRequest, status);
            using var document = JsonDocument.Parse(body);
            ProblemAssert.HasOnlyNameError(document.RootElement, key, ProblemAssert.NameRuleMessage);
            ProblemAssert.NothingDecodedContains(document.RootElement, value, escaped);
        }

        Assert.Equal(before, _db.Dump());
    }
}
