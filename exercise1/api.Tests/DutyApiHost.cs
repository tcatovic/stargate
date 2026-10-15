using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StargateAPI.Tests;

/// <summary>
/// The real HTTP pipeline (WebApplicationFactory, Development) over a per-test temp SQLite database that is migrated
/// and seeded with DevelopmentSeedData here. Never touches starbase.db. Identical in Debug and Release.
/// </summary>
internal sealed class DutyApiHost : IDisposable
{
    public sealed record Reply(HttpStatusCode Status, string Body, JsonElement Json, string? Location);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DutyApiHost(bool seed = true, bool migrate = true)
    {
        Db = new TestDatabase();
        if (seed)
        {
            Db.Dispose();
            Db = TestDatabase.Seeded();
        }
        else if (migrate)
        {
            Db.Migrate();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:StarbaseApiDatabase", Db.ConnectionString);
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public TestDatabase Db { get; private set; }

    public async Task<Reply> Send(HttpMethod method, string url, string? body = null, string? contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (contentType is null)
            {
                request.Content.Headers.ContentType = null;
            }
            else
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var location = response.Headers.Location?.OriginalString;
        if (text.Length == 0)
        {
            return new Reply(response.StatusCode, text, default, location);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return new Reply(response.StatusCode, text, document.RootElement.Clone(), location);
        }
        catch (JsonException)
        {
            return new Reply(response.StatusCode, text, default, location);
        }
    }

    public Task<Reply> Get(string url) => Send(HttpMethod.Get, url);

    public Task<Reply> PostPerson(string name) => Send(HttpMethod.Post, "/Person", JsonSerializer.Serialize(new { name }));

    public Task<Reply> PostDuty(string name, string rank, string dutyTitle, string dutyStartDate) =>
        Send(HttpMethod.Post, "/AstronautDuty", JsonSerializer.Serialize(new { name, rank, dutyTitle, dutyStartDate }));

    /// <summary>Id of the person with this exact stored name.</summary>
    public long PersonId(string name)
    {
        using var context = Db.CreateContext();
        return context.People.Where(p => p.Name == name).Select(p => (long)p.Id).Single();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Db.Dispose();
    }
}
