using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StargateAPI.Business.Commands;
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

        var result = Assert.IsType<ObjectResult>(await WithController(db, c => c.GetPeople(new GetPeople(), CancellationToken.None)));

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
            await WithController(db, c => c.GetPeople(new GetPeople { Name = "priya raman" }, CancellationToken.None))).Value);
        var combined = Assert.IsType<GetPeopleResult>(Assert.IsType<ObjectResult>(
            await WithController(db, c => c.GetPeople(new GetPeople { Rank = "maj", DutyTitle = "commander" }, CancellationToken.None))).Value);

        Assert.Equal(["Priya Raman"], byName.People.Select(p => p.Name));
        Assert.Equal(["Elena Vasquez"], combined.People.Select(p => p.Name));
    }

    [Fact]
    public async Task C5_DatabaseFault_Propagates_InsteadOfBecomingA500Body()
    {
        using var db = new TestDatabase(); // not migrated: no tables

        await Assert.ThrowsAsync<SqliteException>(() => WithController(db, c => c.GetPeople(new GetPeople(), CancellationToken.None)));
    }

    private sealed class CapturingMediator : IMediator
    {
        public CancellationToken? Token { get; private set; }
        public object? Request { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Request = request;
            object result = request switch
            {
                GetPeople => new GetPeopleResult(),
                CreatePerson => new CreatePersonResult { Id = 1, Name = "Ada", ResponseCode = 201 },
                UpdatePerson => new UpdatePersonResult(),
                _ => throw new NotSupportedException(),
            };
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task C6_CreatePerson_PassesTheRequestsCancellationTokenToTheMediator()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new CapturingMediator();

        await new PersonController(mediator).CreatePerson(new CreatePersonRequest { Name = "Ada" }, cts.Token);

        Assert.Equal(cts.Token, mediator.Token);
    }

    [Fact]
    public async Task C7_UpdatePerson_PassesTheRequestsCancellationTokenToTheMediator()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new CapturingMediator();

        await new PersonController(mediator).UpdatePerson(1, new UpdatePersonRequest { Name = "Ada" }, cts.Token);

        Assert.Equal(cts.Token, mediator.Token);
    }

    [Fact]
    public async Task C8_GetPeople_SendsTheVeryObjectItWasBound_WithTheRequestsToken()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new CapturingMediator();
        var query = new GetPeople { Name = "a", Rank = "b", DutyTitle = "c" };

        await new PersonController(mediator).GetPeople(query, cts.Token);

        Assert.Same(query, mediator.Request);
        Assert.Equal(cts.Token, mediator.Token);
    }
}
