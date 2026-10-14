using MediatR;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Data;
using StargateAPI.Business.Queries;

namespace StargateAPI.Tests;

/// <summary>
/// The handlers are called directly on purpose: the guards protect paths that no HTTP request reaches today
/// (the pre-processor rejects unknown names first), so this is the only way to exercise them.
/// </summary>
public class NullPersonGuardTests
{
    private static TestDatabase CreateMigratedDatabase()
    {
        var db = new TestDatabase();
        db.Migrate();
        return db;
    }

    private static void SeedPerson(TestDatabase db, string name, params AstronautDuty[] duties)
    {
        using var context = db.CreateContext();
        var person = new Person { Name = name };
        context.People.Add(person);
        context.SaveChanges();
        foreach (var duty in duties)
        {
            duty.PersonId = person.Id;
            context.AstronautDuties.Add(duty);
        }
        context.SaveChanges();
    }

    [Fact]
    public async Task GetDuties_UnknownName_ReturnsDefaultResultWithoutThrowing()
    {
        using var db = CreateMigratedDatabase();
        SeedPerson(db, "Mira Okafor");
        using var context = db.CreateContext();

        var result = await new GetAstronautDutiesByNameHandler(context)
            .Handle(new GetAstronautDutiesByName { Name = "Nobody" }, CancellationToken.None);

        Assert.Null(result.Person);
        Assert.Empty(result.AstronautDuties);
        Assert.True(result.Success);
        Assert.Equal("Successful", result.Message);
        Assert.Equal(200, result.ResponseCode);
    }

    [Fact]
    public async Task GetDuties_KnownName_ReturnsPersonAndDutiesNewestFirst()
    {
        using var db = CreateMigratedDatabase();
        SeedPerson(db, "Elena Vasquez",
            new AstronautDuty { Rank = "2LT", DutyTitle = "Flight Engineer", DutyStartDate = new DateTime(2012, 1, 9), DutyEndDate = new DateTime(2016, 2, 29) },
            new AstronautDuty { Rank = "MAJ", DutyTitle = "Commander", DutyStartDate = new DateTime(2019, 1, 1) });
        SeedPerson(db, "Other Person",
            new AstronautDuty { Rank = "CPT", DutyTitle = "Pilot", DutyStartDate = new DateTime(2020, 5, 5) });
        using var context = db.CreateContext();

        var result = await new GetAstronautDutiesByNameHandler(context)
            .Handle(new GetAstronautDutiesByName { Name = "Elena Vasquez" }, CancellationToken.None);

        Assert.Equal(200, result.ResponseCode);
        Assert.NotNull(result.Person);
        Assert.Equal("Elena Vasquez", result.Person.Name);
        Assert.Equal(["Commander", "Flight Engineer"], result.AstronautDuties.Select(d => d.DutyTitle).ToArray());
    }

    [Fact]
    public async Task CreateDuty_UnknownPerson_Returns404AndWritesNothing()
    {
        using var db = CreateMigratedDatabase();
        SeedPerson(db, "Mira Okafor");
        var before = db.Dump();
        Assert.Contains("Mira Okafor", before);
        using var context = db.CreateContext();

        var result = await new CreateAstronautDutyHandler(context).Handle(
            new CreateAstronautDuty { Name = "Nobody", Rank = "CPT", DutyTitle = "Pilot", DutyStartDate = new DateTime(2024, 1, 1) },
            CancellationToken.None);

        Assert.Equal(404, result.ResponseCode);
        Assert.False(result.Success);
        Assert.Equal("Person not found", result.Message);
        Assert.DoesNotContain("Nobody", result.Message);
        Assert.Null(result.Id);
        Assert.Equal(before, db.Dump());
    }

    [Fact]
    public async Task CreateDuty_KnownPerson_StillCreatesDetailAndDuty()
    {
        using var db = CreateMigratedDatabase();
        SeedPerson(db, "Kenji Watanabe");
        using var context = db.CreateContext();

        var result = await new CreateAstronautDutyHandler(context).Handle(
            new CreateAstronautDuty { Name = "Kenji Watanabe", Rank = "CPT", DutyTitle = "Pilot", DutyStartDate = new DateTime(2024, 1, 1) },
            CancellationToken.None);

        Assert.Equal(200, result.ResponseCode);
        Assert.True(result.Success);
        Assert.NotNull(result.Id);
        Assert.Equal(1, db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"DutyTitle\" = 'Pilot' AND \"PersonId\" = (SELECT \"Id\" FROM \"Person\" WHERE \"Name\" = 'Kenji Watanabe')"));
        Assert.Equal("Pilot", db.Strings("SELECT \"CurrentDutyTitle\" FROM \"AstronautDetail\"").Single());
    }
}
