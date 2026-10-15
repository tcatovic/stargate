using Dapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;
using System.ComponentModel;

namespace StargateAPI.Business.Queries
{
    /// <summary>
    /// The one definition of the people filters: bound from the query string by MVC and sent to MediatR as is.
    /// Each value is trimmed of ASCII whitespace and then checked against the shared name rule, like the write endpoints.
    /// Null, empty or whitespace-only (MVC binds those to null) means no filter.
    /// </summary>
    public class GetPeople : IRequest<GetPeopleResult>
    {
        [FromQuery(Name = "name"), ValidName]
        [Description("Case-insensitive exact match on the person's full name. Empty or whitespace-only means no filter.")]
        public string? Name { get; init; }

        [FromQuery(Name = "rank"), ValidName]
        [Description("Case-insensitive exact match on the current rank (e.g. MAJ). Excludes people with no assignment.")]
        public string? Rank { get; init; }

        [FromQuery(Name = "dutyTitle"), ValidName]
        [Description("Case-insensitive exact match on the current duty title (e.g. Commander, RETIRED). Excludes people with no assignment.")]
        public string? DutyTitle { get; init; }
    }

    public class GetPeopleHandler : IRequestHandler<GetPeople, GetPeopleResult>
    {
        // Static text: filter values only ever travel as parameters. NULL = no filter. All three filters are
        // case-insensitive (ASCII, NOCASE) exact matches. Rank and duty title compare against the LEFT JOINed columns,
        // so people without an AstronautDetail (rule 2) never match those filters.
        private const string Sql = """
            SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
            FROM [Person] a
            LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
            WHERE (@Name IS NULL OR a.Name = @Name COLLATE NOCASE)
              AND (@Rank IS NULL OR b.CurrentRank = @Rank COLLATE NOCASE)
              AND (@DutyTitle IS NULL OR b.CurrentDutyTitle = @DutyTitle COLLATE NOCASE)
            """;

        public readonly StargateContext _context;
        public GetPeopleHandler(StargateContext context)
        {
            _context = context;
        }
        public async Task<GetPeopleResult> Handle(GetPeople request, CancellationToken cancellationToken)
        {
            var result = new GetPeopleResult();

            var parameters = new
            {
                Name = Normalize(request.Name),
                Rank = Normalize(request.Rank),
                DutyTitle = Normalize(request.DutyTitle),
            };

            var people = await _context.Connection.QueryAsync<PersonAstronaut>(
                new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken));

            result.People = people.ToList();

            return result;
        }

        private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : InputRules.Normalize(value);
    }

    public class GetPeopleResult : BaseResponse
    {
        public List<PersonAstronaut> People { get; set; } = new List<PersonAstronaut> { };

    }
}
