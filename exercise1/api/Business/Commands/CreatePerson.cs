using MediatR;
using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Data;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace StargateAPI.Business.Commands
{
    /// <summary>The POST /Person body. Unknown properties are ignored.</summary>
    public sealed class CreatePersonRequest
    {
        /// <summary>Surrounding ASCII whitespace is removed before validation, comparison and storage.</summary>
        [Required, ValidName]
        public string? Name { get; init; }
    }

    public sealed class CreatePerson : IRequest<CreatePersonResult>
    {
        /// <summary>Raw; the handler normalises.</summary>
        public required string Name { get; init; }
    }

    public sealed class CreatePersonResult : BaseResponse
    {
        /// <summary>The new person's id on 201; null on 409.</summary>
        public int? Id { get; set; }

        /// <summary>The stored name on 201, for the Location header only; null on 409. Never serialised.</summary>
        [JsonIgnore]
        public string? Name { get; set; }
    }

    public sealed class CreatePersonHandler : IRequestHandler<CreatePerson, CreatePersonResult>
    {
        private readonly StargateContext _context;
        private readonly ILogger<CreatePersonHandler> _logger;

        public CreatePersonHandler(StargateContext context, ILogger<CreatePersonHandler> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CreatePersonResult> Handle(CreatePerson request, CancellationToken cancellationToken)
        {
            var name = InputRules.Normalize(request.Name);
            if (!InputRules.IsValidName(name))
            {
                // Unreachable over HTTP (model validation runs first); guards other callers. Never includes the value.
                throw new ArgumentException("Invalid name.", nameof(request));
            }

            // Fast path for the common conflict; the unique index on the NOCASE column is the guarantee.
            var taken = await _context.People.AnyAsync(p => EF.Functions.Collate(p.Name, "NOCASE") == name, cancellationToken);
            if (taken)
            {
                return NameInUse();
            }

            var person = new Person { Name = name };
            _context.People.Add(person);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                // A concurrent create won between the pre-check and the INSERT.
                return NameInUse();
            }

            _logger.LogInformation("Created person {PersonId}", person.Id);
            return new CreatePersonResult { Id = person.Id, Name = name, ResponseCode = StatusCodes.Status201Created };
        }

        private CreatePersonResult NameInUse()
        {
            _logger.LogInformation("Create person rejected: {Reason}", "name in use");
            return new CreatePersonResult
            {
                Id = null,
                Name = null,
                Success = false,
                Message = "A person with this name already exists",
                ResponseCode = StatusCodes.Status409Conflict
            };
        }
    }
}
