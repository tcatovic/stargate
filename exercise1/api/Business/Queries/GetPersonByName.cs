using Dapper;
using MediatR;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;
using System.Net;

namespace StargateAPI.Business.Queries
{
    public class GetPersonByName : IRequest<GetPersonByNameResult>
    {
        public required string Name { get; set; } = string.Empty;
    }

    public class GetPersonByNameHandler : IRequestHandler<GetPersonByName, GetPersonByNameResult>
    {
        // Exact, ASCII-case-insensitive match. Until names are unique case-insensitively (task 002), an exact-case
        // match wins, then the lowest Id, so the answer is deterministic.
        private const string Sql = """
            SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
            FROM [Person] a
            LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
            WHERE a.Name = @Name COLLATE NOCASE
            ORDER BY CASE WHEN a.Name = @Name THEN 0 ELSE 1 END, a.Id
            LIMIT 1
            """;

        private readonly StargateContext _context;
        public GetPersonByNameHandler(StargateContext context)
        {
            _context = context;
        }

        public async Task<GetPersonByNameResult> Handle(GetPersonByName request, CancellationToken cancellationToken)
        {
            var people = await _context.Connection.QueryAsync<PersonAstronaut>(
                new CommandDefinition(Sql, new { Name = InputRules.Normalize(request.Name) }, cancellationToken: cancellationToken));

            var person = people.FirstOrDefault();
            if (person is null)
            {
                // The message never echoes the input.
                return new GetPersonByNameResult
                {
                    Person = null,
                    Success = false,
                    Message = "Person not found",
                    ResponseCode = (int)HttpStatusCode.NotFound
                };
            }

            return new GetPersonByNameResult { Person = person };
        }
    }

    public class GetPersonByNameResult : BaseResponse
    {
        public PersonAstronaut? Person { get; set; }
    }
}
