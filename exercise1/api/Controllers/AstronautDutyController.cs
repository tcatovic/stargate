using MediatR;
using Microsoft.AspNetCore.Mvc;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Queries;
using StargateAPI.Business.Validation;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace StargateAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AstronautDutyController : ControllerBase
    {
        private readonly IMediator _mediator;
        public AstronautDutyController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("")]
        [EndpointSummary("Get one person and their duties by exact name (case-insensitive)")]
        [EndpointDescription("The name is required, trimmed, validated against the shared name rule and matched exactly, ignoring ASCII case "
            + "(no contains, no wildcards). Returns the person and all of their duties, newest start date first. A person with no astronaut "
            + "assignment returns an empty duty list. An unknown name returns 404.")]
        [ProducesResponseType(typeof(GetAstronautDutiesByNameResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(GetAstronautDutiesByNameResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAstronautDutiesByName(
            [FromQuery, Required, ValidName, Description("The person's full name, URL-encoded (e.g. Elena%20Vasquez, Aoife%20O%27Brien%20Jr.).")] string? name,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetAstronautDutiesByName()
            {
                Name = name ?? string.Empty
            }, cancellationToken);

            return this.GetResponse(result);
        }

        [HttpPost("")]
        [Consumes("application/json")]
        [RequestSizeLimit(4096)]
        [EndpointSummary("Add a duty to a person")]
        [EndpointDescription("Adds a duty for the person whose name equals name (trimmed, exact, ignoring ASCII case). name, rank and dutyTitle "
            + "are validated against the shared name rule; rank and dutyTitle are stored trimmed. dutyStartDate is required (only its date part is used) "
            + "and must be between 1900-01-01 and 2100-12-31. The person's current duty ends the day before the new one starts. "
            + "An unknown person is 404. The same title (ignoring case) and start date for the same person is 409. "
            + "Returns 201 with the new duty's id and a Location header /AstronautDuty?name=<URL-encoded stored name>.")]
        [ProducesResponseType(typeof(CreateAstronautDutyResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(CreateAstronautDutyResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(CreateAstronautDutyResult), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateAstronautDuty([FromBody] CreateAstronautDutyRequest request, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new CreateAstronautDuty
            {
                Name = request.Name ?? string.Empty,
                Rank = request.Rank ?? string.Empty,
                DutyTitle = request.DutyTitle ?? string.Empty,
                DutyStartDate = request.DutyStartDate.GetValueOrDefault()
            }, cancellationToken);

            return result.ResponseCode == StatusCodes.Status201Created && result.Name is not null
                ? Created(DutyLocation(result.Name), result)
                : this.GetResponse(result);
        }

        // Relative and percent-encoded (header values must be ASCII; names contain spaces and apostrophes).
        // Built from the stored name, not the request's casing or padding.
        private static string DutyLocation(string storedName) => "/AstronautDuty?name=" + Uri.EscapeDataString(storedName);
    }
}
