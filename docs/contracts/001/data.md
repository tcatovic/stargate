# Task 001: data contract

## Schema changes

None. No migration, no new index, no constraint change. The query reads `Person` and `AstronautDetail` as they are after task 003 (`AstronautDetail.CareerStartDate NOT NULL`). The case-insensitive unique index on `Person.Name` belongs to task 002.

## Shared input rule (new file `exercise1/api/Business/Validation/InputRules.cs`)

```csharp
namespace StargateAPI.Business.Validation
{
    /// <summary>Shared bounds for free-text identifiers (person name, rank, duty title). Reused by tasks 002 and 006.</summary>
    public static class InputRules
    {
        public const int MaxLength = 100;
        // Unicode letters (+ combining marks), ASCII digits, space, period, apostrophe (U+0027), hyphen.
        public const string AllowedCharactersPattern = @"^[\p{L}\p{M}0-9 .'-]+$";
        public const string AllowedCharactersMessage =
            "Use only letters, digits, spaces, hyphens, apostrophes and periods (at most 100 characters).";
    }
}
```

Applied in `PersonController` as action-parameter attributes; `[ApiController]` turns failures into a 400 `ValidationProblemDetails` before any handler runs:

| Parameter | Binding | Attributes |
|---|---|---|
| `name` (GetPeople) | `[FromQuery] string?` | `[StringLength(InputRules.MaxLength)]`, `[RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = InputRules.AllowedCharactersMessage)]`, `[Description(...)]` |
| `rank` (GetPeople) | `[FromQuery] string?` | same |
| `dutyTitle` (GetPeople) | `[FromQuery] string?` | same |
| `name` (GetPersonByName) | `[FromRoute] string` | `[StringLength(InputRules.MaxLength, MinimumLength = 1)]`, same `[RegularExpression]`, `[Description(...)]` |

Every action also takes a `CancellationToken cancellationToken` and passes it to `_mediator.Send`. Length is measured on the raw (untrimmed) value. Empty or whitespace-only query values bind as `null` (no filter).

## Requests and handlers

```csharp
public class GetPeople : IRequest<GetPeopleResult>
{
    public string? Name { get; init; }       // null = no filter
    public string? Rank { get; init; }
    public string? DutyTitle { get; init; }
}
// GetPersonByName unchanged: public required string Name { get; set; }
// Result classes unchanged: GetPeopleResult { List<PersonAstronaut> People }, GetPersonByNameResult { PersonAstronaut? Person }.
```

Handlers do not validate; they normalise and stay safe for any input (direct MediatR callers, tests):

- `Normalize(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()`
- `EscapeLike(string v) => v.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")` (backslash first)
- Dapper `QueryAsync<PersonAstronaut>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))`. Parameters are an anonymous object or `DynamicParameters`; values are never placed in the SQL text. SQL strings are verbatim/raw C# literals so `'\'` reaches SQLite unchanged.

### GetPeople SQL (static text)

```sql
SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
FROM [Person] a
LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
WHERE (@NamePattern IS NULL OR a.Name LIKE @NamePattern ESCAPE '\')
  AND (@Rank IS NULL OR b.CurrentRank = @Rank COLLATE NOCASE)
  AND (@DutyTitle IS NULL OR b.CurrentDutyTitle = @DutyTitle COLLATE NOCASE)
```

Parameters: `NamePattern = name is null ? null : "%" + EscapeLike(name) + "%"`, `Rank = Normalize(rank)`, `DutyTitle = Normalize(dutyTitle)`. No ORDER BY (order unspecified, as today). Result: `Success = true`, `Message = "Successful"`, `ResponseCode = 200`, `People` = rows (possibly empty).

### GetPersonByName SQL (static text)

```sql
SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
FROM [Person] a
LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
WHERE a.Name = @Name COLLATE NOCASE
ORDER BY CASE WHEN a.Name = @Name THEN 0 ELSE 1 END, a.Id
LIMIT 1
```

Parameter: `Name = request.Name.Trim()` (an empty or whitespace-only name matches nobody → 404). Found → `Person = row`, defaults (`true`, `"Successful"`, `200`). Not found → `Person = null`, `Success = false`, `Message = "Person not found"`, `ResponseCode = (int)HttpStatusCode.NotFound`.

## Case-insensitivity semantics (SQLite)

`LIKE` (with default `case_sensitive_like = OFF`) and `COLLATE NOCASE` fold ASCII A-Z only. A non-ASCII letter matches only in the same case (`élena` does not find `Élena`). This is the accepted behaviour (plan Q3), pinned by test P11, and matches the NOCASE semantics task 002's unique index will use.

## How each domain rule is enforced here

| Rule | Enforcement |
|---|---|
| 1. Name identifies a person | Lookup is exact (`=`, not LIKE) and case-insensitive (`COLLATE NOCASE`), consistent with task 002's planned NOCASE unique index. Pre-002 case-duplicates resolve deterministically (exact case, then lowest Id). Read-only: no write races introduced. |
| 2. No assignment → no astronaut rows | LEFT JOIN yields NULL `CurrentRank`/`CurrentDutyTitle`; `NULL = @x COLLATE NOCASE` is never true, so rank/dutyTitle filters exclude unassigned people; the unfiltered and name-only queries include them. Empty filter values are normalised to "no filter", so `""` never matches. Response keeps `""` for their rank/title (Dapper leaves the DTO defaults) and null dates. |

## Controller

- `GetPeople` and `GetPersonByName` actions: no `try/catch`; `return this.GetResponse(result);` (status = `result.ResponseCode`). Unhandled exceptions propagate to the host (500, empty body in Production, logged by Kestrel).
- Swagger attributes per action: `[EndpointSummary]`, `[EndpointDescription]`, `[ProducesResponseType(typeof(GetPeopleResult) | typeof(GetPersonByNameResult), 200)]`, `[ProducesResponseType(typeof(ValidationProblemDetails), 400)]`, `GetPersonByName` also `[ProducesResponseType(typeof(GetPersonByNameResult), 404)]`, both `[ProducesResponseType(StatusCodes.Status500InternalServerError)]`. No XML doc generation (would break the warnings-as-errors build on CS1591).
- `CreatePerson` and `AstronautDutyController` are not edited.
