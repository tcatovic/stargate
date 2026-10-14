using Dapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace StargateAPI.Business.Commands
{
    /// <summary>The PUT /Person/{personId} body. Unknown properties are ignored.</summary>
    public sealed class UpdatePersonRequest
    {
        /// <summary>New name. Surrounding whitespace is removed and the value is NFC-normalised before validation, comparison and storage.</summary>
        [Required, ValidName]
        public string? Name { get; init; }
    }

    public sealed class UpdatePerson : IRequest<UpdatePersonResult>
    {
        public required int PersonId { get; init; }

        /// <summary>Raw; the handler normalises.</summary>
        public required string Name { get; init; }
    }

    public sealed class UpdatePersonResult : BaseResponse
    {
        public PersonAstronaut? Person { get; set; }
    }

    public sealed class UpdatePersonHandler : IRequestHandler<UpdatePerson, UpdatePersonResult>
    {
        // Same projection as GET /Person, for one person. Static text: the id only travels as a parameter.
        private const string ReadBackSql = """
            SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
            FROM [Person] a
            LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
            WHERE a.Id = @PersonId
            """;

        private readonly StargateContext _context;
        private readonly ILogger<UpdatePersonHandler> _logger;

        public UpdatePersonHandler(StargateContext context, ILogger<UpdatePersonHandler> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<UpdatePersonResult> Handle(UpdatePerson request, CancellationToken cancellationToken)
        {
            var name = InputRules.Normalize(request.Name);
            if (!InputRules.IsValidName(name))
            {
                // Unreachable over HTTP (model validation runs first); guards other callers. Never includes the value.
                throw new ArgumentException("Invalid name.", nameof(request));
            }

            var person = await _context.People.SingleOrDefaultAsync(p => p.Id == request.PersonId, cancellationToken);
            if (person is null)
            {
                _logger.LogInformation("Rename of person {PersonId} rejected: {Reason}", request.PersonId, "not found");
                return new UpdatePersonResult
                {
                    Success = false,
                    Message = "Person not found",
                    ResponseCode = (int)HttpStatusCode.NotFound,
                    Person = null
                };
            }

            // Fast path for the common conflict; the unique index on the NOCASE column is the guarantee.
            var taken = await _context.People.AnyAsync(
                p => p.Id != request.PersonId && EF.Functions.Collate(p.Name, "NOCASE") == name, cancellationToken);
            if (taken)
            {
                return NameInUse(request.PersonId);
            }

            person.Name = name;
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                return NameInUse(request.PersonId);
            }

            var updated = await _context.Connection.QueryAsync<PersonAstronaut>(
                new CommandDefinition(ReadBackSql, new { request.PersonId }, cancellationToken: cancellationToken));

            var row = updated.FirstOrDefault();
            if (row is null)
            {
                // Deleted between the update and the read-back (no delete endpoint exists yet).
                _logger.LogInformation("Rename of person {PersonId} rejected: {Reason}", request.PersonId, "not found");
                return new UpdatePersonResult
                {
                    Success = false,
                    Message = "Person not found",
                    ResponseCode = (int)HttpStatusCode.NotFound,
                    Person = null
                };
            }

            _logger.LogInformation("Renamed person {PersonId}", request.PersonId);
            return new UpdatePersonResult { Person = row };
        }

        private UpdatePersonResult NameInUse(int personId)
        {
            _logger.LogInformation("Rename of person {PersonId} rejected: {Reason}", personId, "name in use");
            return new UpdatePersonResult
            {
                Success = false,
                Message = "A person with this name already exists",
                ResponseCode = (int)HttpStatusCode.Conflict,
                Person = null
            };
        }
    }
}
