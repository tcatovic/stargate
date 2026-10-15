# Task 006: data contract (revision 2)

Markdown because agents read and edit it as text. Baseline: `main` at `f42dc41`.

Revision 2 changes:
- Section "Shared name rule" is rewritten: names are ASCII-only, NFC and the invisible set are removed, and so is the raw bound.
- New section "Exact name filter" (`GetPeople.cs`).
- The handler no longer has a raw-bound step.
- The README text has been extended.
- The "Known limits" paragraph is replaced (compatibility characters are moot).

## Schema change

None. There is no migration and no snapshot change, and `dotnet ef migrations has-pending-model-changes` must report no changes.

The task relies on what task 002 delivered:
- `Person.Name` is `TEXT NOT NULL COLLATE NOCASE`, with the unique index `IX_Person_Name`.
- `SqliteErrors.IsUniqueViolation` recognises a violation of that index (extended code 2067).

The applied migration `20261014233740_AddUniquePersonNameIndex` is **not edited**. It still trims the 25 Unicode whitespace code points from names that existed before it ran. That is harmless and already shipped, but its comment "which is what the app trims" is historical after this task. Its tests M1-M13 stay unchanged and green, because they do not reference `InputRules`.

## Shared name rule (commit set C1): `exercise1/api/Business/Validation/InputRules.cs`

This replaces the whole class body. The public names `MaxLength`, `AllowedCharactersPattern`, `AllowedCharactersMessage`, `Normalize` and `IsValidName` stay, so callers compile unchanged. `MaxRawLength` and the private `Invisible` are deleted.

```csharp
/// <summary>
/// Shared bounds for free-text identifiers (person name, rank, duty title), defined once for every endpoint.
/// Names are ASCII-only: <see cref="Normalize"/> trims ASCII whitespace, then a value must be 1 to
/// <see cref="MaxLength"/> characters of A-Z, a-z, 0-9, space, period, apostrophe and hyphen.
/// No Unicode normalisation: any non-ASCII character, including non-ASCII whitespace at an edge, is invalid.
/// </summary>
public static class InputRules
{
    public const int MaxLength = 100;

    // Written out (never \d or \p{L}, which match non-ASCII digits and letters) and used WITHOUT
    // RegexOptions.IgnoreCase (under which U+212A KELVIN SIGN matches k). \z, not $, so a trailing newline never matches.
    public const string AllowedCharactersPattern = @"^[A-Za-z0-9 .'-]+\z";

    public const string AllowedCharactersMessage =
        "Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods (at most 100 characters).";

    // Tab, LF, VT, FF, CR, space. Deliberately not string.Trim()'s Unicode set: a non-ASCII character is never
    // silently removed, so it reaches IsValidName and is rejected.
    private static readonly char[] AsciiWhitespace = ['\t', '\n', '\v', '\f', '\r', ' '];

    private static readonly Regex AllowedCharacters = new(
        AllowedCharactersPattern,
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Removes leading and trailing ASCII whitespace. Never throws.</summary>
    public static string Normalize(string value) => value.Trim(AsciiWhitespace);

    /// <summary>True when the value has 1 to <see cref="MaxLength"/> characters and matches the pattern. Expects the output of <see cref="Normalize"/>.</summary>
    public static bool IsValidName(string normalized)
    {
        if (normalized.Length is < 1 or > MaxLength)
        {
            return false;
        }

        try
        {
            return AllowedCharacters.IsMatch(normalized);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
```

Remove `using System.Text;` from the file if it becomes unused.

### `ValidNameAttribute.cs`

The body becomes:

```csharp
public override bool IsValid(object? value) =>
    value is null || (value is string text && InputRules.IsValidName(InputRules.Normalize(text)));
```

Update the doc comment so it no longer mentions NFC or the raw bound.

### `UpdatePerson.cs`

Line 56 becomes `var name = InputRules.Normalize(request.Name);`. Update the comment on line 55, and the `UpdatePersonRequest.Name` doc comment ("Surrounding ASCII whitespace is removed before validation, comparison and storage."). Nothing else changes.

### `PersonController.cs` (PUT)

In the `EndpointDescription`, replace "The name is NFC-normalised and trimmed, then validated" with "Surrounding ASCII whitespace is removed, then the name is validated". Replace "after that normalisation" with "after trimming".

### Callers that stay as they are

- `GetPeople` (its private `Normalize` helper) and `GetPersonByName.cs:38` keep calling `InputRules.Normalize`, which now means an ASCII trim.
- The GET `[StringLength(InputRules.MaxLength)]` and `[RegularExpression(InputRules.AllowedCharactersPattern)]` attributes keep validating the raw value. They pick up the new constants automatically.

### Behaviour table (normative)

| Input as sent | Normalize | Valid |
| --- | --- | --- |
| `"  Ada \t"`, `"\r\nAda\r\n"` | `"Ada"` | yes |
| `"Tomas  Lindqvist"` | unchanged (inner spaces kept) | yes |
| 3 spaces + 100 letters + 3 spaces | 100 letters | yes |
| 101 letters | unchanged | no |
| `""`, `"   "`, `"\t"` | `""` | no |
| `" Ada"`, `"Ada　"`, `"\u0085Ada"`, `"Ada "` | unchanged | **no** |
| `"Zoë"`, `"José"`, `"Émile"`, `"Renée"`, `"Ａｄａ"`, `"Аda"` (U+0410), `"Ada 🚀"`, `"O’Brien"` (U+2019), `"Kate"` (Kelvin), `"ſam"`, `"٣"`, `"Ada​"`, `"Adaㅤ"` | unchanged | **no** |
| `"a\tb"`, `"Ada\n"` (raw pattern), `"<script>"`, `"%"`, `"_"`, `"a;b"` | (inner unchanged) | no |
| `"O'Brien-Smith Jr."`, `"2LT"`, `"RETIRED"`, every seed name, rank and title | unchanged | yes |

## Exact name filter (commit set C2): `exercise1/api/Business/Queries/GetPeople.cs`

```csharp
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
// parameters: new { Name = Normalize(request.Name), Rank = Normalize(request.Rank), DutyTitle = Normalize(request.DutyTitle) }
```

- Delete `EscapeLike` and its comment.
- Change the `GetPeople.Name` doc comment to "Case-insensitive exact match on the full name; null, empty or whitespace means no filter."

In `PersonController.GetPeople`:

- The `EndpointDescription` sentence "name is a case-insensitive contains match; rank and dutyTitle are case-insensitive exact matches..." becomes "name, rank and dutyTitle are case-insensitive exact matches (name on the full name; rank and dutyTitle on the current AstronautDetail values)...".
- The `name` parameter `Description` becomes "Case-insensitive exact match on the person's full name. Empty or whitespace-only means no filter."
- Nothing else changes in the response shape, validation or attributes.

## POST (commit set C3): `exercise1/api/Business/Commands/CreatePerson.cs`

```csharp
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
```

Delete `CreatePersonPreProcessor` and the `using MediatR.Pipeline;` line. `Program.cs` stays as it is.

### Handler (`CreatePersonHandler(StargateContext context, ILogger<CreatePersonHandler> logger)`)

Each step passes `cancellationToken`.

1. `var name = InputRules.Normalize(request.Name);`
2. If `!InputRules.IsValidName(name)`, throw `new ArgumentException("Invalid name.", nameof(request))`. This cannot happen over HTTP, and the message never includes the value.
3. If `await _context.People.AnyAsync(p => EF.Functions.Collate(p.Name, "NOCASE") == name, ct)` is true, return `NameInUse()`. No INSERT is attempted.
4. `var person = new Person { Name = name }; _context.People.Add(person);`
5. `await _context.SaveChangesAsync(ct)`. Catch `DbUpdateException` only `when (SqliteErrors.IsUniqueViolation(ex))` and return `NameInUse()`. Every other exception propagates.
6. `_logger.LogInformation("Created person {PersonId}", person.Id)`.
7. Return `new CreatePersonResult { Id = person.Id, Name = name, ResponseCode = 201 }`. `Success` and `Message` keep their defaults (`true`, `"Successful"`).

`NameInUse()` logs `LogInformation("Create person rejected: {Reason}", "name in use")` and returns `new CreatePersonResult { Id = null, Name = null, Success = false, Message = "A person with this name already exists", ResponseCode = 409 }`.

Nothing logs the name. The handler never touches `AstronautDetail` or `AstronautDuty`. There is a single `SaveChanges`, so no explicit transaction is needed.

### Controller action (replaces `PersonController.cs:102-125`; no try/catch)

```csharp
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
```

Remove `using System.Net;` from the controller if it becomes unused.

## How each rule is enforced

| Rule | Code | Database | Tests |
| --- | --- | --- | --- |
| 1. A Person is uniquely identified by Name | `[ValidName]`. The handler trims ASCII whitespace and runs the ASCII rule, then a NOCASE pre-check (409). Lookup by name is exact NOCASE | Unique index `IX_Person_Name` on the `COLLATE NOCASE` column. With ASCII-only names, NOCASE equality is complete case-insensitivity. A lost race maps to 409 | H4, H5, H6, H7, P4, P11, P15, F1-F3 |
| 2. No assignment means no AstronautDetail or AstronautDuty rows | The create handler adds only `Person` | FKs (unchanged) | H9, P14 |
| Input: names are ASCII (not a spec rule) | `InputRules` on every endpoint | none (by decision; plan Question 1) | R4b, R11, R21, R25, X1 |

## README (`exercise1/api/README.md`, "Changelog (breaking changes)")

Insert these three bullets at the top, in this order. Each one is added by its own commit set.

> - Task 006 (BREAKING, C3): `POST /Person` takes a JSON object `{ "name": "..." }` (Content-Type `application/json`, body at most 4096 bytes). A bare JSON string is no longer accepted: 400 with `application/json`, 415 otherwise. Success is now **201 Created** (was 200) with body `{ id, success, message, responseCode }` (`responseCode` 201) and `Location: /Person?name=<URL-encoded stored name>`, which returns exactly that person. Invalid names are 400 (the value is never echoed). A name equal to an existing one ignoring ASCII case and surrounding whitespace is **409** `{ id: null, success: false, message: "A person with this name already exists", responseCode: 409 }`, including concurrent creates (never a 500, never two rows). 415 for a non-JSON content type, 413 for a larger body. Unexpected errors return 500 with no exception text (empty body in Production). A new person has no astronaut records.
> - Task 006 (BREAKING, C2): `GET /Person?name=` is now a case-insensitive **exact** match on the full name (it was a contains match). `?name=ra` and `?name=elena` now return an empty list; `?name=elena%20vasquez` returns Elena Vasquez. `rank` and `dutyTitle` are unchanged.
> - Task 006 (BREAKING, C1): person names (and the `name`, `rank` and `dutyTitle` values checked by the same rule) are **ASCII-only**: letters A-Z and a-z, digits, space, hyphen, apostrophe and period, 1 to 100 characters after trimming. Any other character returns 400 on `GET /Person`, `GET /Person/{name}`, `PUT /Person/{personId}` and `POST /Person`. That includes accented letters such as `Émile`, which were valid before, NFD input, and non-ASCII whitespace such as a no-break space, even at the start or end. Only ASCII whitespace (space, tab, CR, LF) is trimmed. Names are no longer NFC-normalised. The validation message now reads "Use only English letters (A-Z), digits, ...". Names already stored with non-ASCII characters remain and are listed, but cannot be looked up by name; rename them by id.

Append these sentences to existing bullets:
- The bullet starting "Task 002 (BEHAVIOUR CHANGE, interim until task 006)" gets: " Superseded by task 006: duplicates are now 409 and names are trimmed."
- The bullet starting "Task 002 (not breaking for ASCII input)" gets: " Superseded by task 006 (C1): names are ASCII-only and no longer NFC-normalised."
- The bullet starting "Task 001 (not breaking): `GET /Person` accepts optional `name` (case-insensitive contains)" gets: " Superseded by task 006 (C2): `name` is exact."

In the Database "Notes", append to the `AddUniquePersonNameIndex` bullet: " Since task 006 the API trims ASCII whitespace only and rejects non-ASCII names; the migration's wider trim applied once to existing rows."

Make no other README change.
