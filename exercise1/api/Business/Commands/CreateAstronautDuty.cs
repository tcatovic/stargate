using MediatR;
using Microsoft.EntityFrameworkCore;
using StargateAPI.Business.Data;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace StargateAPI.Business.Commands
{
    /// <summary>
    /// The POST /AstronautDuty body. Members are nullable and have no <c>required</c> keyword so a missing member is a
    /// field message from model validation, not a parser error. Unknown properties are ignored.
    /// </summary>
    public sealed class CreateAstronautDutyRequest
    {
        /// <summary>The person's full name; trimmed, then matched exactly, ignoring ASCII case.</summary>
        [Required, ValidName]
        public string? Name { get; init; }

        /// <summary>Stored trimmed.</summary>
        [Required, ValidName]
        public string? Rank { get; init; }

        /// <summary>Stored trimmed.</summary>
        [Required, ValidName]
        public string? DutyTitle { get; init; }

        /// <summary>Only the date part is used; it must be between 1900-01-01 and 2100-12-31.</summary>
        [Required, ValidDutyDate]
        public DateTime? DutyStartDate { get; init; }
    }

    public sealed class CreateAstronautDuty : IRequest<CreateAstronautDutyResult>
    {
        /// <summary>Raw; the handler normalises.</summary>
        public required string Name { get; init; }

        /// <summary>Raw; the handler normalises.</summary>
        public required string Rank { get; init; }

        /// <summary>Raw; the handler normalises.</summary>
        public required string DutyTitle { get; init; }

        public required DateTime DutyStartDate { get; init; }
    }

    public sealed class CreateAstronautDutyHandler : IRequestHandler<CreateAstronautDuty, CreateAstronautDutyResult>
    {
        private readonly StargateContext _context;
        private readonly ILogger<CreateAstronautDutyHandler> _logger;

        public CreateAstronautDutyHandler(StargateContext context, ILogger<CreateAstronautDutyHandler> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CreateAstronautDutyResult> Handle(CreateAstronautDuty request, CancellationToken cancellationToken)
        {
            var name = InputRules.Normalize(request.Name);
            var rank = InputRules.Normalize(request.Rank);
            var title = InputRules.Normalize(request.DutyTitle);
            if (!InputRules.IsValidName(name) || !InputRules.IsValidName(rank) || !InputRules.IsValidName(title)
                || !InputRules.IsValidDutyDate(request.DutyStartDate))
            {
                // Unreachable over HTTP (model validation runs first); guards other callers. Never includes a value.
                throw new ArgumentException("Invalid duty request.", nameof(request));
            }

            var start = request.DutyStartDate.Date;

            // The person is resolved exactly once. Every later read and write uses only person.Id, so the name can
            // never widen the match, and every value below reaches SQLite as a parameter (EF LINQ only, no raw SQL).
            var person = await _context.People.AsNoTracking()
                .Where(p => EF.Functions.Collate(p.Name, "NOCASE") == name)
                // Same order as GetAstronautDutiesByName: an exact-case match first, then the lowest Id
                // (names are only unique ignoring case once AddUniquePersonNameIndex has been applied).
                .OrderBy(p => EF.Functions.Collate(p.Name, "BINARY") == name ? 0 : 1)
                .ThenBy(p => p.Id)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(cancellationToken);
            if (person is null)
            {
                _logger.LogInformation("Create duty rejected: {Reason}", "person not found");
                return new CreateAstronautDutyResult
                {
                    Id = null,
                    Name = null,
                    Success = false,
                    Message = "Person not found",
                    ResponseCode = StatusCodes.Status404NotFound
                };
            }

            // Scoped to this person; date part only; title compared ignoring ASCII case.
            var duplicate = await _context.AstronautDuties.AsNoTracking().AnyAsync(d =>
                d.PersonId == person.Id
                && EF.Functions.Collate(d.DutyTitle, "NOCASE") == title
                && d.DutyStartDate.Date == start, cancellationToken);
            if (duplicate)
            {
                _logger.LogInformation("Create duty rejected: {Reason} for person {PersonId}", "duplicate", person.Id);
                return Conflict("A duty with this title and start date already exists for this person");
            }

            // Rules 3-7 as they behaved before this task (known defects are task 009's).
            var detail = await _context.AstronautDetails.FirstOrDefaultAsync(d => d.PersonId == person.Id, cancellationToken);
            if (detail is null)
            {
                detail = new AstronautDetail
                {
                    PersonId = person.Id,
                    CurrentDutyTitle = title,
                    CurrentRank = rank,
                    CareerStartDate = start
                };
                if (title == "RETIRED")
                {
                    // Known defect (rule 7 says start minus 1 day), left for task 009.
                    detail.CareerEndDate = start;
                }
                _context.AstronautDetails.Add(detail);
            }
            else
            {
                detail.CurrentDutyTitle = title;
                detail.CurrentRank = rank;
                if (title == "RETIRED")
                {
                    detail.CareerEndDate = start.AddDays(-1);
                }
            }

            // Id as tie-break so "the latest duty" is deterministic when two share a start date.
            var latest = await _context.AstronautDuties
                .Where(d => d.PersonId == person.Id)
                .OrderByDescending(d => d.DutyStartDate)
                .ThenByDescending(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                latest.DutyEndDate = start.AddDays(-1);
            }

            var duty = new AstronautDuty
            {
                PersonId = person.Id,
                Rank = rank,
                DutyTitle = title,
                DutyStartDate = start,
                DutyEndDate = null
            };
            _context.AstronautDuties.Add(duty);

            try
            {
                // One save: EF writes every row in a single transaction.
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                // Two first duties for one person raced; IX_AstronautDetail_PersonId is the only unique index this write can hit.
                _logger.LogInformation("Create duty rejected: {Reason} for person {PersonId}", "concurrent change", person.Id);
                return Conflict("The person's duties were changed by another request");
            }

            _logger.LogInformation("Created duty {DutyId} for person {PersonId}", duty.Id, person.Id);
            return new CreateAstronautDutyResult { Id = duty.Id, Name = person.Name, ResponseCode = StatusCodes.Status201Created };
        }

        private static CreateAstronautDutyResult Conflict(string message) => new()
        {
            Id = null,
            Name = null,
            Success = false,
            Message = message,
            ResponseCode = StatusCodes.Status409Conflict
        };
    }

    public sealed class CreateAstronautDutyResult : BaseResponse
    {
        /// <summary>The new duty's id on 201; null on 404 and 409.</summary>
        public int? Id { get; set; }

        /// <summary>The person's stored name on 201, for the Location header only; null otherwise. Never serialised.</summary>
        [JsonIgnore]
        public string? Name { get; set; }
    }
}
