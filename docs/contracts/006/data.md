# Task 006: data contract (revision 1)

Markdown because agents read and edit it as text. Baseline: `main` at `f42dc41`.

## Schema change

None. No migration, no snapshot change. `dotnet ef migrations has-pending-model-changes` must report no changes.
The task relies on what task 002 delivered:

- `Person.Name` is `TEXT NOT NULL COLLATE NOCASE` with the unique index `IX_Person_Name`.
- A violation surfaces as `DbUpdateException` with inner `SqliteException { SqliteExtendedErrorCode: 2067 }`. The existing `SqliteErrors.IsUniqueViolation` recognises it.

## Shared name rule

Unchanged. Reuse it and do not copy it:

- `InputRules.MaxRawLength` (800)
- `InputRules.Normalize` (NFC, then `string.Trim()`, never throws)
- `InputRules.IsValidName` (1..100 characters plus `AllowedCharactersPattern`)
- `[ValidName]`

No new pattern, no new message, no new constant.

Known and accepted limits, out of scope here (see plan Question 2):

- NFC, not NFKC: fullwidth `Ａｄａ` and ligatures such as `ﬁ` are distinct from their ASCII forms.
- Cross-script look-alikes are distinct names.
- `NOCASE` folds ASCII only: `Émile` and `émile` are different names.
- Rows that existed before task 002 were not NFC-normalised by its migration.

## C# shapes (`exercise1/api/Business/Commands/CreatePerson.cs`)

```csharp
/// <summary>The POST /Person body. Unknown properties are ignored.</summary>
public sealed class CreatePersonRequest
{
    /// <summary>Surrounding whitespace is removed and the value is NFC-normalised before validation, comparison and storage.</summary>
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

    /// <summary>The stored (normalised) name on 201, for the Location header only; null on 409. Never serialised.</summary>
    [JsonIgnore]
    public string? Name { get; set; }
}
```

`CreatePersonPreProcessor` is **deleted**, together with `using MediatR.Pipeline;`. `Program.cs` is unchanged, because it never registered the pre-processor.

### Handler (`CreatePersonHandler`, constructor `(StargateContext context, ILogger<CreatePersonHandler> logger)`)

The steps run in this order, and each one passes `cancellationToken`:

1. Bound and normalise: `var name = request.Name.Length <= InputRules.MaxRawLength ? InputRules.Normalize(request.Name) : string.Empty;`
2. If `!InputRules.IsValidName(name)`, throw `new ArgumentException("Invalid name.", nameof(request))`. This is unreachable over HTTP. The message never contains the value.
3. Pre-check: `await _context.People.AnyAsync(p => EF.Functions.Collate(p.Name, "NOCASE") == name, ct)`. If true, return `NameInUse()`. No INSERT is attempted.
4. `var person = new Person { Name = name }; _context.People.Add(person);`
5. `await _context.SaveChangesAsync(ct)`.
   - `catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))` returns `NameInUse()`.
   - Every other exception propagates, including other `DbUpdateException`s and `OperationCanceledException`.
6. `_logger.LogInformation("Created person {PersonId}", person.Id)`.
7. Return `new CreatePersonResult { Id = person.Id, Name = name, ResponseCode = 201 }`. `Success` (true) and `Message` ("Successful") keep their defaults.

`NameInUse()` does two things:

- Logs `_logger.LogInformation("Create person rejected: {Reason}", "name in use")`.
- Returns `new CreatePersonResult { Id = null, Name = null, Success = false, Message = "A person with this name already exists", ResponseCode = 409 }`.

The name is never logged at any level.

The handler never reads or writes `AstronautDetail` or `AstronautDuty`. There is a single `SaveChanges`, so no explicit transaction is needed.

## Controller (`PersonController`)

The POST action replaces lines 102-125. It has no try/catch.

```csharp
[HttpPost("")]
[Consumes("application/json")]
[RequestSizeLimit(4096)]
[EndpointSummary("Create a person")]
[EndpointDescription("Creates a person with no astronaut assignment. The name is NFC-normalised and trimmed, then validated "
    + "against the shared name rule, compared and stored. Names are unique ignoring ASCII case (SQLite NOCASE) after that "
    + "normalisation; a taken name is 409. Returns 201 with the new id and a Location header /Person?name=<encoded stored name>.")]
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

// Relative, ASCII-only: Kestrel rejects non-ASCII header values with a 500 after the row is committed.
// /Person?name= (not /Person/{name}) so it still resolves after task 008 removes the by-name route.
private static string PersonLocation(string storedName) => "/Person?name=" + Uri.EscapeDataString(storedName);
```

- `Created(...)` produces an `ObjectResult` with status 201, so `responseCode` (201) and the HTTP status agree.
- If `using System.Net;` becomes unused, remove it, or the warnings-as-errors build fails on IDE0005.
- The global binding-message overrides in `Program.cs` already apply to the new body DTO.

If the user keeps the original task decision (`/Person/{name}`), the only change is `PersonLocation` returning `"/Person/" + Uri.EscapeDataString(storedName)`, plus the expected header values in tests P1, P2, P3 and K1.

## How each domain rule is enforced

| Rule | Code | Database | Tests |
| --- | --- | --- | --- |
| 1. A Person is uniquely identified by Name | `[ValidName]` at the edge. In the handler: `Normalize` (NFC + trim), then a NOCASE pre-check (409) | Unique index `IX_Person_Name` on the `COLLATE NOCASE` column. A lost race is mapped to 409 by `IsUniqueViolation` | H4, H5, H6, H7, P4, P11, P15 |
| 2. No assignment means no AstronautDetail/AstronautDuty rows | The handler adds only `Person` | FKs only (unchanged) | H9, P14 |

Rules 3-7 are not touched.

## README (`exercise1/api/README.md`, "Changelog (breaking changes)")

Insert this as the **first** bullet:

> - Task 006 (BREAKING): `POST /Person` takes a JSON object `{ "name": "..." }` (Content-Type `application/json`, body at most 4096 bytes). A bare JSON string is no longer accepted: 400 with `application/json`, 415 otherwise. Success is now **201 Created** (was 200) with body `{ id, success, message, responseCode }` (`responseCode` 201) and `Location: /Person?name=<URL-encoded stored name>`. Names are NFC-normalised and trimmed and must pass the shared name rule (400 otherwise, the value is never echoed). A name equal to an existing one ignoring ASCII case and surrounding whitespace is **409** `{ id: null, success: false, message: "A person with this name already exists", responseCode: 409 }`, including concurrent creates (never a 500, never two rows). 415 for a non-JSON content type, 413 for a larger body. Unexpected errors return 500 with no exception text (empty body in Production). A new person has no astronaut records.

Append to the existing bullet that starts "Task 002 (BEHAVIOUR CHANGE, interim until task 006)":

> Superseded by task 006: duplicates are now 409 and names are trimmed and normalised.

Make no other README change.
