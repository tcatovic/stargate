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
    public class PersonController : ControllerBase
    {
        private readonly IMediator _mediator;
        public PersonController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("")]
        [EndpointSummary("List people, optionally filtered by name, current rank and current duty title")]
        [EndpointDescription("Filters combine with AND; an omitted, empty or whitespace-only filter does not filter. "
            + "Surrounding ASCII whitespace is removed from each value, then the value is validated against the shared name rule. "
            + "name, rank and dutyTitle are case-insensitive exact matches (name on the full name; rank and dutyTitle "
            + "on the current AstronautDetail values). People with no astronaut assignment are listed when no rank or "
            + "dutyTitle filter is given and are never matched by one. Case-insensitivity folds ASCII letters only. "
            + "Order is unspecified; no paging.")]
        [ProducesResponseType(typeof(GetPeopleResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPeople(
            // Name = "" pins the model prefix to empty. Without it, a request that contains a key equal to the parameter
            // name (?query=x) switches MVC to the "query." prefix, which silently drops every filter and its validation.
            [FromQuery(Name = "")] GetPeople query,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(query, cancellationToken);

            return this.GetResponse(result);
        }

        [HttpPut("{personId}")]
        [Consumes("application/json")]
        [RequestSizeLimit(4096)]
        [EndpointSummary("Rename a person")]
        [EndpointDescription("Renames the person with this id. Surrounding ASCII whitespace is removed, then the name is validated against the shared "
            + "name rule, compared and stored. Names are unique ignoring ASCII case (SQLite NOCASE) after trimming; "
            + "renaming to the person's own name or a different casing of it succeeds. Idempotent: repeating the same "
            + "request yields the same state and the same 200 body. Only Person.Name changes; AstronautDetail and "
            + "AstronautDuty rows are never read or written. No Location header.")]
        [ProducesResponseType(typeof(UpdatePersonResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UpdatePersonResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(UpdatePersonResult), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdatePerson(
            [FromRoute, Range(1, int.MaxValue), Description("The person's id (personId in GET /Person results).")] int personId,
            [FromBody] UpdatePersonRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new UpdatePerson()
            {
                PersonId = personId,
                Name = request.Name ?? string.Empty
            }, cancellationToken);

            return this.GetResponse(result);
        }

        [HttpPost("")]
        [Consumes("application/json")]
        [RequestSizeLimit(4096)]
        [EndpointSummary("Create a person")]
        [EndpointDescription("Creates a person with no astronaut assignment. Surrounding ASCII whitespace is removed, then the name is "
            + "validated against the shared name rule (ASCII letters, digits, space, hyphen, apostrophe, period; 1 to 100 characters), "
            + "compared and stored. Names are unique ignoring ASCII case; a taken name is 409. Returns 201 with the new id and a "
            + "Location header /Person?name=<URL-encoded stored name>.")]
        [ProducesResponseType(typeof(CreatePersonResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(CreatePersonResult), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreatePerson([FromBody] CreatePersonRequest request, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new CreatePerson { Name = request.Name ?? string.Empty }, cancellationToken);

            return result.ResponseCode == StatusCodes.Status201Created && result.Name is not null
                ? Created(PersonLocation(result.Name), result)
                : this.GetResponse(result);
        }

        // Relative and percent-encoded (header values must be ASCII; names contain spaces and apostrophes).
        // The exact name filter returns exactly this person, and the URL survives task 008.
        private static string PersonLocation(string storedName) => "/Person?name=" + Uri.EscapeDataString(storedName);
    }
}
