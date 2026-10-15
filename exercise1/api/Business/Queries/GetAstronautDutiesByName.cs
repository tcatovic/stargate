using Dapper;
using MediatR;
using StargateAPI.Business.Data;
using StargateAPI.Business.Dtos;
using StargateAPI.Business.Validation;
using StargateAPI.Controllers;

namespace StargateAPI.Business.Queries
{
    public class GetAstronautDutiesByName : IRequest<GetAstronautDutiesByNameResult>
    {
        public string Name { get; set; } = string.Empty;
    }

    public class GetAstronautDutiesByNameHandler : IRequestHandler<GetAstronautDutiesByName, GetAstronautDutiesByNameResult>
    {
        // Static SQL: the name and the id only ever travel as parameters. The person SQL is a copy of
        // GetPersonByName's on purpose (task 008 deletes that type); the order (exact-case match first, then lowest Id) matches it and the create handler.
        private const string PersonSql = """
            SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
            FROM [Person] a
            LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
            WHERE a.Name = @Name COLLATE NOCASE
            ORDER BY CASE WHEN a.Name = @Name COLLATE BINARY THEN 0 ELSE 1 END, a.Id
            LIMIT 1
            """;

        // @PersonId is the id PersonSql returned, never anything from the request.
        private const string DutiesSql = """
            SELECT Id, PersonId, Rank, DutyTitle, DutyStartDate, DutyEndDate
            FROM [AstronautDuty]
            WHERE PersonId = @PersonId
            ORDER BY DutyStartDate DESC, Id DESC
            """;

        private readonly StargateContext _context;

        public GetAstronautDutiesByNameHandler(StargateContext context)
        {
            _context = context;
        }

        public async Task<GetAstronautDutiesByNameResult> Handle(GetAstronautDutiesByName request, CancellationToken cancellationToken)
        {
            var people = await _context.Connection.QueryAsync<PersonAstronaut>(
                new CommandDefinition(PersonSql, new { Name = InputRules.Normalize(request.Name) }, cancellationToken: cancellationToken));

            var person = people.FirstOrDefault();
            if (person is null)
            {
                // The message never echoes the input.
                return new GetAstronautDutiesByNameResult
                {
                    Person = null,
                    Success = false,
                    Message = "Person not found",
                    ResponseCode = StatusCodes.Status404NotFound
                };
            }

            var duties = await _context.Connection.QueryAsync<AstronautDutyItem>(
                new CommandDefinition(DutiesSql, new { PersonId = person.PersonId }, cancellationToken: cancellationToken));

            return new GetAstronautDutiesByNameResult { Person = person, AstronautDuties = duties.ToList() };
        }
    }

    public class GetAstronautDutiesByNameResult : BaseResponse
    {
        public PersonAstronaut? Person { get; set; }
        public List<AstronautDutyItem> AstronautDuties { get; set; } = new List<AstronautDutyItem>();
    }
}
