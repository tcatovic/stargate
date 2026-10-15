using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StargateAPI.Business.Data;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

/// <summary>The single GetPeople query object carries the shared name rule on each of its three properties.</summary>
public class GetPeopleValidationTests
{
    private static List<ValidationResult> Validate(GetPeople query)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), results, validateAllProperties: true);
        return results;
    }

    public static TheoryData<string> Properties => new() { "Name", "Rank", "DutyTitle" };

    private static GetPeople With(string property, string? value) => property switch
    {
        "Name" => new GetPeople { Name = value },
        "Rank" => new GetPeople { Rank = value },
        "DutyTitle" => new GetPeople { DutyTitle = value },
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    [Theory]
    [MemberData(nameof(Properties))]
    public void V1_EachPropertyAlone_RejectsAPercentSign_WithTheNameRuleMessage(string property)
    {
        var result = Assert.Single(Validate(With(property, "%")));

        Assert.Equal(new[] { property }, result.MemberNames.ToArray());
        Assert.Equal(ProblemAssert.NameRuleMessage, result.ErrorMessage);
    }

    [Fact]
    public void V2_NoFilters_AreValid() => Assert.Empty(Validate(new GetPeople()));

    [Theory]
    [MemberData(nameof(Properties))]
    public void V2_EachPropertyAlone_AcceptsATrimmedValue_AndPaddedHundredCharacters(string property)
    {
        Assert.Empty(Validate(With(property, "\t Elena Vasquez \n")));
        Assert.Empty(Validate(With(property, "  " + new string('a', 100) + "\t")));
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void V2_EachPropertyAlone_Rejects101Characters(string property)
    {
        var result = Assert.Single(Validate(With(property, new string('a', 101))));

        Assert.Equal(ProblemAssert.NameRuleMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task V3_GetPeople_IsTheMediatRRequest_AndItsHandlerRunsThroughSend()
    {
        Assert.True(typeof(IRequest<GetPeopleResult>).IsAssignableFrom(typeof(GetPeople)));
        using var db = PeopleFixture.Create();
        var services = new ServiceCollection();
        services.AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetPeople).Assembly));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetPeople { Name = "elena vasquez" });

        Assert.Equal(new[] { "Elena Vasquez" }, result.People.Select(p => p.Name).ToArray());
    }
}
