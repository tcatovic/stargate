using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StargateAPI.Business.Data;
using StargateAPI.Business.Queries;
using StargateAPI.Controllers;

namespace StargateAPI.Tests;

public class PersonControllerTests
{
    private static ServiceProvider Services(TestDatabase db)
    {
        var services = new ServiceCollection();
        services.AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PersonController).Assembly));
        return services.BuildServiceProvider();
    }

    private static async Task<T> WithController<T>(TestDatabase db, Func<PersonController, Task<T>> act)
    {
        await using var provider = Services(db);
        using var scope = provider.CreateScope();
        return await act(new PersonController(scope.ServiceProvider.GetRequiredService<IMediator>()));
    }

    [Fact]
    public async Task C1_GetPeople_NoFilters_Is200WithEveryone()
    {
        using var db = PeopleFixture.Create();

        var result = Assert.IsType<ObjectResult>(await WithController(db, c => c.GetPeople(null, null, null, CancellationToken.None)));

        var body = Assert.IsType<GetPeopleResult>(result.Value);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(6, body.People.Count);
        Assert.Equal(result.StatusCode, body.ResponseCode);
    }

    [Fact]
    public async Task C2_GetPeople_ForwardsEachFilter()
    {
        using var db = PeopleFixture.Create();

        var byName = Assert.IsType<GetPeopleResult>(Assert.IsType<ObjectResult>(
            await WithController(db, c => c.GetPeople("priya raman", null, null, CancellationToken.None))).Value);
        var combined = Assert.IsType<GetPeopleResult>(Assert.IsType<ObjectResult>(
            await WithController(db, c => c.GetPeople(null, "maj", "commander", CancellationToken.None))).Value);

        Assert.Equal(["Priya Raman"], byName.People.Select(p => p.Name));
        Assert.Equal(["Elena Vasquez"], combined.People.Select(p => p.Name));
    }

    [Fact]
    public async Task C3_GetPersonByName_Found_Is200()
    {
        using var db = PeopleFixture.Create();

        var result = Assert.IsType<ObjectResult>(await WithController(db, c => c.GetPersonByName("mira okafor", CancellationToken.None)));

        var body = Assert.IsType<GetPersonByNameResult>(result.Value);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(200, body.ResponseCode);
        Assert.Equal("Mira Okafor", body.Person?.Name);
    }

    [Fact]
    public async Task C4_GetPersonByName_Unknown_Is404()
    {
        using var db = PeopleFixture.Create();

        var result = Assert.IsType<ObjectResult>(await WithController(db, c => c.GetPersonByName("Nobody", CancellationToken.None)));

        var body = Assert.IsType<GetPersonByNameResult>(result.Value);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(404, body.ResponseCode);
        Assert.False(body.Success);
        Assert.Null(body.Person);
    }

    [Fact]
    public async Task C5_DatabaseFault_Propagates_InsteadOfBecomingA500Body()
    {
        using var db = new TestDatabase(); // not migrated: no tables

        await Assert.ThrowsAsync<SqliteException>(() => WithController(db, c => c.GetPeople(null, null, null, CancellationToken.None)));
        await Assert.ThrowsAsync<SqliteException>(() => WithController(db, c => c.GetPersonByName("x", CancellationToken.None)));
    }
}
