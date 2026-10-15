using MediatR;
using Microsoft.AspNetCore.Mvc;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Queries;
using StargateAPI.Controllers;

namespace StargateAPI.Tests;

public class AstronautDutyControllerTests
{
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
                GetAstronautDutiesByName => new GetAstronautDutiesByNameResult(),
                CreateAstronautDuty => new CreateAstronautDutyResult { Id = 1, Name = "Kenji Watanabe", ResponseCode = 201 },
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
    public async Task C1_GetAstronautDutiesByName_SendsTheDutiesQuery_WithTheRequestsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new CapturingMediator();

        var result = Assert.IsType<ObjectResult>(await new AstronautDutyController(mediator).GetAstronautDutiesByName("Elena Vasquez", cts.Token));

        Assert.Equal(cts.Token, mediator.Token);
        Assert.Equal("Elena Vasquez", Assert.IsType<GetAstronautDutiesByName>(mediator.Request).Name);
        Assert.Equal(200, result.StatusCode);
        Assert.IsType<GetAstronautDutiesByNameResult>(result.Value);
    }

    [Fact]
    public async Task C2_CreateAstronautDuty_PassesTheTokenAndTheRequestValues_AndBuildsTheEscapedLocation()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new CapturingMediator();

        var result = await new AstronautDutyController(mediator).CreateAstronautDuty(
            new CreateAstronautDutyRequest { Name = "Kenji Watanabe", Rank = "CPT", DutyTitle = "Pilot", DutyStartDate = new DateTime(2024, 1, 1) }, cts.Token);

        Assert.Equal(cts.Token, mediator.Token);
        var sent = Assert.IsType<CreateAstronautDuty>(mediator.Request);
        Assert.Equal(("Kenji Watanabe", "CPT", "Pilot", new DateTime(2024, 1, 1)), (sent.Name, sent.Rank, sent.DutyTitle, sent.DutyStartDate));
        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal("/AstronautDuty?name=Kenji%20Watanabe", created.Location);
        Assert.Equal(201, created.StatusCode);
    }
}
