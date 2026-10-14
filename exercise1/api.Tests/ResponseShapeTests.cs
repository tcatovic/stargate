using System.Text.Json;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

/// <summary>
/// Pins the JSON property names and value kinds of the success responses. MVC's default is JsonSerializerDefaults.Web.
/// Note: GET /AstronautDuty/{name} currently serves the person result (the controller calls GetPersonByName), so its
/// over-the-wire shape is the GetPersonByNameResult shape below. The true over-the-wire shapes are checked by QA
/// until the integration-test task adds WebApplicationFactory.
/// </summary>
public class ResponseShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static SortedDictionary<string, JsonValueKind> Kinds(JsonElement element) =>
        new(element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind), StringComparer.Ordinal);

    private static SortedDictionary<string, JsonValueKind> Expect(params (string Name, JsonValueKind Kind)[] fields) =>
        new(fields.ToDictionary(f => f.Name, f => f.Kind), StringComparer.Ordinal);

    private static readonly (string, JsonValueKind)[] Envelope =
    [
        ("success", JsonValueKind.True), ("message", JsonValueKind.String), ("responseCode", JsonValueKind.Number)
    ];

    private static SortedDictionary<string, JsonValueKind> WithEnvelope(params (string Name, JsonValueKind Kind)[] fields) =>
        Expect(fields.Concat(Envelope).ToArray());

    private static SortedDictionary<string, JsonValueKind> PersonShape(JsonValueKind careerStart, JsonValueKind careerEnd) => Expect(
        ("personId", JsonValueKind.Number), ("name", JsonValueKind.String), ("currentRank", JsonValueKind.String),
        ("currentDutyTitle", JsonValueKind.String), ("careerStartDate", careerStart), ("careerEndDate", careerEnd));

    [Fact]
    public void GetAstronautDutiesByNameResult_HasTheDocumentedShape()
    {
        var result = new GetAstronautDutiesByNameResult
        {
            Person = new PersonAstronaut { PersonId = 1, Name = "Elena Vasquez", CurrentRank = "MAJ", CurrentDutyTitle = "Commander", CareerStartDate = new DateTime(2012, 1, 9) },
            // Dapper-read duties never populate the Person navigation.
            AstronautDuties =
            [
                new AstronautDuty { Id = 3, PersonId = 1, Rank = "MAJ", DutyTitle = "Commander", DutyStartDate = new DateTime(2019, 1, 1) },
                new AstronautDuty { Id = 2, PersonId = 1, Rank = "CPT", DutyTitle = "Pilot", DutyStartDate = new DateTime(2016, 3, 1), DutyEndDate = new DateTime(2018, 12, 31) }
            ]
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        var root = json.RootElement;

        Assert.Equal(WithEnvelope(("person", JsonValueKind.Object), ("astronautDuties", JsonValueKind.Array)), Kinds(root));
        Assert.Equal(PersonShape(JsonValueKind.String, JsonValueKind.Null), Kinds(root.GetProperty("person")));
        var current = Expect(
            ("id", JsonValueKind.Number), ("personId", JsonValueKind.Number), ("rank", JsonValueKind.String), ("dutyTitle", JsonValueKind.String),
            ("dutyStartDate", JsonValueKind.String), ("dutyEndDate", JsonValueKind.Null), ("person", JsonValueKind.Null));
        var ended = Expect(
            ("id", JsonValueKind.Number), ("personId", JsonValueKind.Number), ("rank", JsonValueKind.String), ("dutyTitle", JsonValueKind.String),
            ("dutyStartDate", JsonValueKind.String), ("dutyEndDate", JsonValueKind.String), ("person", JsonValueKind.Null));
        Assert.Equal(current, Kinds(root.GetProperty("astronautDuties")[0]));
        Assert.Equal(ended, Kinds(root.GetProperty("astronautDuties")[1]));
    }

    [Theory]
    [InlineData(true, JsonValueKind.Object)]
    [InlineData(false, JsonValueKind.Null)]
    public void GetPersonByNameResult_HasTheDocumentedShape_AndWritesNullPerson(bool withPerson, JsonValueKind expectedPersonKind)
    {
        var result = new GetPersonByNameResult
        {
            Person = withPerson ? new PersonAstronaut { PersonId = 1, Name = "Mira Okafor" } : null
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));

        Assert.Equal(WithEnvelope(("person", expectedPersonKind)), Kinds(json.RootElement));
        if (withPerson)
        {
            Assert.Equal(PersonShape(JsonValueKind.Null, JsonValueKind.Null), Kinds(json.RootElement.GetProperty("person")));
        }
    }

    [Fact]
    public void CreateAstronautDutyResult_HasTheDocumentedShape()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new CreateAstronautDutyResult { Id = 9 }, Web));
        Assert.Equal(WithEnvelope(("id", JsonValueKind.Number)), Kinds(json.RootElement));
        Assert.Equal(9, json.RootElement.GetProperty("id").GetInt32());

        using var noId = JsonDocument.Parse(JsonSerializer.Serialize(new CreateAstronautDutyResult(), Web));
        Assert.Equal(JsonValueKind.Null, noId.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task GetPeopleResult_FromTheRealHandler_HasTheDocumentedShape()
    {
        using var db = new TestDatabase();
        db.Migrate();
        using (var seed = db.CreateContext())
        {
            seed.People.Add(new Person { Name = "Mira Okafor" });
            seed.People.Add(new Person
            {
                Name = "Desmond Achebe",
                AstronautDetail = new AstronautDetail
                {
                    CurrentRank = "LTC",
                    CurrentDutyTitle = "RETIRED",
                    CareerStartDate = new DateTime(2008, 2, 4),
                    CareerEndDate = new DateTime(2023, 3, 31)
                }
            });
            seed.SaveChanges();
        }
        using var context = db.CreateContext();

        var result = await new GetPeopleHandler(context).Handle(new GetPeople(), CancellationToken.None);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(WithEnvelope(("people", JsonValueKind.Array)), Kinds(json.RootElement));
        var people = json.RootElement.GetProperty("people").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        Assert.Equal(2, people.Count);
        // Unassigned person: empty strings and null dates, never omitted.
        Assert.Equal(PersonShape(JsonValueKind.Null, JsonValueKind.Null), Kinds(people["Mira Okafor"]));
        Assert.Equal(PersonShape(JsonValueKind.String, JsonValueKind.String), Kinds(people["Desmond Achebe"]));
    }
}
