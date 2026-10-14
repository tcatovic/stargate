using System.Text.Json;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

/// <summary>Pins the JSON property names of the success responses. MVC's default is JsonSerializerDefaults.Web.</summary>
public class ResponseShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] Sorted(params string[] names) => names.Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void GetAstronautDutiesByNameResult_HasTheDocumentedShape()
    {
        var result = new GetAstronautDutiesByNameResult
        {
            Person = new PersonAstronaut { PersonId = 1, Name = "Elena Vasquez", CurrentRank = "MAJ", CurrentDutyTitle = "Commander" },
            // Dapper-read duties never populate the Person navigation.
            AstronautDuties = [new AstronautDuty { Id = 3, PersonId = 1, Rank = "MAJ", DutyTitle = "Commander", DutyStartDate = new DateTime(2019, 1, 1) }]
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        var root = json.RootElement;

        Assert.Equal(Sorted("person", "astronautDuties", "success", "message", "responseCode"), Names(root));
        Assert.Equal(Sorted("personId", "name", "currentRank", "currentDutyTitle", "careerStartDate", "careerEndDate"), Names(root.GetProperty("person")));
        var duty = root.GetProperty("astronautDuties")[0];
        Assert.Equal(Sorted("id", "personId", "rank", "dutyTitle", "dutyStartDate", "dutyEndDate", "person"), Names(duty));
        Assert.Equal(JsonValueKind.Null, duty.GetProperty("person").ValueKind);
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

        Assert.Equal(Sorted("person", "success", "message", "responseCode"), Names(json.RootElement));
        Assert.Equal(expectedPersonKind, json.RootElement.GetProperty("person").ValueKind);
    }

    [Fact]
    public void CreateAstronautDutyResult_HasTheDocumentedShape()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new CreateAstronautDutyResult { Id = 9 }, Web));

        Assert.Equal(Sorted("id", "success", "message", "responseCode"), Names(json.RootElement));
        Assert.Equal(9, json.RootElement.GetProperty("id").GetInt32());
    }
}
