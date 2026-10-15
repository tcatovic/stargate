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
        public async Task<IActionResult> CreateAstronautDuty([FromBody] CreateAstronautDuty request)
        {
                var result = await _mediator.Send(request);
                return this.GetResponse(result);           
        }
    }
}
