using MediatR;
using Microsoft.AspNetCore.Mvc;
using StargateAPI.Business.Commands;
using StargateAPI.Business.Queries;
using StargateAPI.Business.Validation;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;

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
            + "Values are trimmed. name, rank and dutyTitle are case-insensitive exact matches (name on the full name; rank and dutyTitle "
            + "on the current AstronautDetail values). People with no astronaut assignment are listed when no rank or "
            + "dutyTitle filter is given and are never matched by one. Case-insensitivity folds ASCII letters only. "
            + "Order is unspecified; no paging.")]
        [ProducesResponseType(typeof(GetPeopleResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPeople(
            [FromQuery, StringLength(InputRules.MaxLength), RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = InputRules.AllowedCharactersMessage),
                Description("Case-insensitive exact match on the person's full name. Empty or whitespace-only means no filter.")] string? name,
            [FromQuery, StringLength(InputRules.MaxLength), RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = InputRules.AllowedCharactersMessage),
                Description("Case-insensitive exact match on the current rank (e.g. MAJ). Excludes people with no assignment.")] string? rank,
            [FromQuery, StringLength(InputRules.MaxLength), RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = InputRules.AllowedCharactersMessage),
                Description("Case-insensitive exact match on the current duty title (e.g. Commander, RETIRED). Excludes people with no assignment.")] string? dutyTitle,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetPeople()
            {
                Name = name,
                Rank = rank,
                DutyTitle = dutyTitle
            }, cancellationToken);

            return this.GetResponse(result);
        }

        [HttpGet("{name}")]
        [EndpointSummary("Get one person by exact name (case-insensitive)")]
        [EndpointDescription("The route value is URL-decoded and trimmed, then matched exactly, ignoring ASCII case. "
            + "% and _ have no wildcard meaning. An unknown name returns 404.")]
        [ProducesResponseType(typeof(GetPersonByNameResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(GetPersonByNameResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPersonByName(
            [FromRoute, StringLength(InputRules.MaxLength, MinimumLength = 1), RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = InputRules.AllowedCharactersMessage),
                Description("The person's full name, URL-encoded (e.g. Mira%20Okafor, O%27Brien).")] string name,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetPersonByName()
            {
                Name = name
            }, cancellationToken);

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
        public async Task<IActionResult> CreatePerson([FromBody] string name)
        {
            try
            {
                var result = await _mediator.Send(new CreatePerson()
                {
                    Name = name
                });

                return this.GetResponse(result);
            }
            catch (Exception ex)
            {
                return this.GetResponse(new BaseResponse()
                {
                    Message = ex.Message,
                    Success = false,
                    ResponseCode = (int)HttpStatusCode.InternalServerError
                });
            }

        }
    }
}