# Task 002: data contract (revision 1, Amendment 1 incl. 1b)

Markdown because agents read and edit it as text. Human summary: `contract.html`.

Amendment 1 (documentation only, no behaviour change) changed these sections:
- "Program.cs": the two non-property binding-message accessors.
- "Shared name rule": the invisible set is now a code-point table instead of literal characters, and the trim set is defined.
- "Request, handler and result": step 5 handles an empty read-back as 404.
- "Controller": 415 bodies, and JSON property order.

Amendment 1b records behaviour added during the review round (merge `55ae823`). It is non-breaking:
- The migration trims and guards on the full .NET whitespace set (25 code points), and aborts on a name that is empty once trimmed.
- `MaxRawLength = 800`: raw input over 800 characters is rejected before normalisation.
- `[RequestSizeLimit(4096)]`: an over-size body gets 413 on Kestrel.
- `DbUpdateConcurrencyException` maps to 404.
- Validation order is documented.

## Schema change

| Object | Before | After |
|---|---|---|
| `Person.Name` | `TEXT NOT NULL` (BINARY collation) | `TEXT COLLATE NOCASE NOT NULL` |
| `IX_Person_Name` | none | `CREATE UNIQUE INDEX "IX_Person_Name" ON "Person" ("Name")` (inherits NOCASE from the column) |

Model (`exercise1/api/Business/Data/Person.cs`, `PersonConfiguration.Configure`), added after the existing lines:

```csharp
builder.Property(x => x.Name).UseCollation("NOCASE");
builder.HasIndex(x => x.Name).IsUnique();
```

No other entity changes. No `HasMaxLength` (SQLite ignores it; it would only add noise to the rebuild).

## Migration `AddUniquePersonNameIndex`

Generate, never hand-write the Designer/snapshot:

```bash
cd exercise1/api
dotnet ef migrations add AddUniquePersonNameIndex -o Business/Migrations --namespace StargateAPI.Migrations
```

`--namespace` is required: without it the generator uses `StargateAPI.Business.Migrations` (verified), unlike the existing migrations.

Then add a public constant to the migration class, and prepend two `migrationBuilder.Sql` calls at the top of the generated `Up`, in this order, before the generated `AlterColumn` and `CreateIndex`. Amendment 1b replaced the revision 1 trim set `char(32, 9, 10, 13)` with the constant.

```csharp
/// The UTF-16 code points that .NET string.Trim() removes (char.IsWhiteSpace). A test asserts this list equals that set exactly.
public const string TrimCharacterCodes =
    "9, 10, 11, 12, 13, 32, 133, 160, 5760, 8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202, 8232, 8233, 8239, 8287, 12288";

// Rule 1 (task 002): abort, changing nothing, if names collide ignoring ASCII case and surrounding whitespace
// (the same whitespace set string.Trim() removes), or if any name is empty once trimmed.
// Must run before EF's table rebuild, which commits around PRAGMA foreign_keys: EF also creates the unique index on the
// old BINARY column first and the NOCASE index only after the committed rebuild, so without this guard case-only
// duplicates would fail half-way. SQLite-specific and transitional (same pattern as MakeCareerStartDateRequired).
migrationBuilder.Sql(
    $"""
    CREATE TEMP TABLE "__guard_PersonName" ("x" INTEGER);
    CREATE TEMP TRIGGER "__guard_PersonName_check" BEFORE INSERT ON "__guard_PersonName"
    WHEN EXISTS (SELECT 1 FROM main."Person" GROUP BY trim("Name", char({TrimCharacterCodes})) COLLATE NOCASE HAVING COUNT(*) > 1)
      OR EXISTS (SELECT 1 FROM main."Person" WHERE trim("Name", char({TrimCharacterCodes})) = '')
    BEGIN
        SELECT RAISE(ABORT, 'AddUniquePersonNameIndex: some Person names are equal ignoring ASCII case and leading or trailing whitespace, or are empty once trimmed, so the unique index on Person.Name cannot be created. No data was changed. List them with: SELECT Id, Name FROM Person ORDER BY lower(trim(Name)); then rename or merge those people (back up the database before changing data). Only for a disposable development database: delete starbase.db and restart.');
    END;
    INSERT INTO "__guard_PersonName" ("x") VALUES (1);
    DROP TABLE "__guard_PersonName";
    """);

// Names are trimmed before they are stored (task 002 decision). Safe: the guard proved no collision after trimming.
migrationBuilder.Sql(
    $"""UPDATE "Person" SET "Name" = trim("Name", char({TrimCharacterCodes})) WHERE "Name" <> trim("Name", char({TrimCharacterCodes}));""");
```

Requirements on the message (tests assert these substrings): `AddUniquePersonNameIndex`, `No data was changed`, `back up`, and (Amendment 1b, M13) `empty once trimmed`. It must contain no single quote other than the delimiters (SQL string literal). The suggested listing query uses SQLite's default `trim()`, which removes only U+0020. It is a sorting aid for the operator, not the guard's test.

`Down`: exactly as generated (`DropIndex IX_Person_Name`, `AlterColumn` removing the collation). It does not un-trim names.

Expected effects (verified by tests on SQLite with EF 10.0.13):
- Duplicates (`Ada` with `"  ADA "`, or `Ada` with `"Ada"` padded by any whitespace in the set, M12) or a name that is empty once trimmed (`''`, a single space, U+3000 followed by a tab; M13): `SqliteException` (wrapped by EF) with the message above. `__EFMigrationsHistory`, all rows (still untrimmed), the `Person` DDL and the indexes are unchanged, and no `ef_temp_%` or `__guard_%` objects are left.
- No duplicates: every one of the 25 whitespace characters is trimmed at both ends (M10; for example U+3000 + `Ada` becomes `Ada`, and `Grace` + U+00A0 becomes `Grace`, M11). The `Person` DDL contains `"Name" TEXT COLLATE NOCASE NOT NULL`, `IX_Person_Name` is unique, and `pragma_index_xinfo('IX_Person_Name')` reports `coll = 'NOCASE'` for `Name`. Ids and all `AstronautDetail`/`AstronautDuty` rows are unchanged.
- EF logs a warning that the rebuild "cannot be executed in a transaction" (same as task 003). Expected; not an error.

Existing rows are not NFC-normalised (not possible in SQLite SQL). Documented residual risk.

## Shared name rule (`exercise1/api/Business/Validation/InputRules.cs`)

```csharp
public static class InputRules
{
    public const int MaxLength = 100;

    // Amendment 1b: raw input longer than this is rejected before Normalize runs, so normalisation never works on
    // unbounded input. 8 x MaxLength: 100 characters in the worst decomposed form fit well within it.
    public const int MaxRawLength = 8 * MaxLength;   // 800

    // The invisible set below (table), as regex escapes, never as literal characters. Shown here as a regular C#
    // string with doubled backslashes so this document contains no invisible characters. InputRules.cs writes
    // the same regex text as a verbatim string with single backslashes; both give the regex the same text.
    private const string Invisible = "\\u034F\\u115F\\u1160\\u17B4\\u17B5\\u180B-\\u180F\\u3164\\uFE00-\\uFE0F\\uFFA0";

    public const string AllowedCharactersPattern =
        @"^(?:(?![" + Invisible + @"])\p{L}(?:(?![" + Invisible + @"])\p{M}){0,3}|[0-9 .'-])+\z";

    public const string AllowedCharactersMessage =
        "Use only letters, digits, spaces, hyphens, apostrophes and periods (at most 100 characters)."; // unchanged

    /// NFC (NormalizationForm.FormC), then string.Trim(). Never throws: if the value is ill-formed UTF-16
    /// (string.Normalize throws ArgumentException), returns value.Trim() unnormalised; it then fails the pattern.
    public static string Normalize(string value);

    /// normalized.Length is >= 1 and <= MaxLength, and the pattern matches (CultureInvariant; 100 ms timeout or [GeneratedRegex]).
    /// Expects the output of Normalize; does not normalise again.
    public static bool IsValidName(string normalized);
}
```

Supplementary-plane letters (for example U+1D400, a surrogate pair) are rejected, as on `main`. `\p{L}` sees only the two surrogate halves (R22).

### Invisible set (28 code points)

These code points are excluded both as the letter and as a following mark. They are the same set as `NamePattern` in `openapi.yaml`. `InputRulesTests.R13c` checks every one of the 28.

| Code point(s) | Name | Category | Count |
|---|---|---|---|
| U+034F | COMBINING GRAPHEME JOINER | Mn | 1 |
| U+115F | HANGUL CHOSEONG FILLER | Lo | 1 |
| U+1160 | HANGUL JUNGSEONG FILLER | Lo | 1 |
| U+17B4 | KHMER VOWEL INHERENT AQ | Mn | 1 |
| U+17B5 | KHMER VOWEL INHERENT AA | Mn | 1 |
| U+180B - U+180D | MONGOLIAN FREE VARIATION SELECTOR ONE - THREE | Mn | 3 |
| U+180E | MONGOLIAN VOWEL SEPARATOR | Cf (see note) | 1 |
| U+180F | MONGOLIAN FREE VARIATION SELECTOR FOUR | Mn | 1 |
| U+3164 | HANGUL FILLER | Lo | 1 |
| U+FE00 - U+FE0F | VARIATION SELECTOR-1 - VARIATION SELECTOR-16 | Mn | 16 |
| U+FFA0 | HALFWIDTH HANGUL FILLER | Lo | 1 |
| | | **Total** | **28** |

Note on U+180E: it is in the set only because the range `U+180B-U+180F` is contiguous. It is a format character (Cf), not a letter or mark, so the base pattern rejects it anyway. `string.Trim()` does not remove it on .NET 10, because `char.IsWhiteSpace((char)0x180E)` is false (verified on .NET 10.0.13). So a leading, trailing or inner U+180E always gives 400 and is never silently dropped. It is also not in the migration's trim set.

### Trim set (Amendment 1b: the API and the migration use the same set)

The whitespace set is the 25 UTF-16 code points for which `char.IsWhiteSpace` is true. These are exactly what `string.Trim()` removes, and exactly `TrimCharacterCodes`. Test M9 asserts that equality.

| Code point(s) | Decimal | Name |
|---|---|---|
| U+0009 - U+000D | 9 - 13 | TAB, LF, VT, FF, CR |
| U+0020 | 32 | SPACE |
| U+0085 | 133 | NEXT LINE |
| U+00A0 | 160 | NO-BREAK SPACE |
| U+1680 | 5760 | OGHAM SPACE MARK |
| U+2000 - U+200A | 8192 - 8202 | EN QUAD ... HAIR SPACE (11) |
| U+2028 | 8232 | LINE SEPARATOR |
| U+2029 | 8233 | PARAGRAPH SEPARATOR |
| U+202F | 8239 | NARROW NO-BREAK SPACE |
| U+205F | 8287 | MEDIUM MATHEMATICAL SPACE |
| U+3000 | 12288 | IDEOGRAPHIC SPACE |
| | | **Total 25** |

- **API** (`Normalize`): `string.Trim()` removes these at both ends (R21: U+00A0 + `Ada` + U+2003 becomes `Ada`). Zero-width and format characters (U+200B, U+180E, U+FEFF) are not whitespace. They stay in the value and fail the pattern.
- **Migration**: trims the same 25 at both ends (M10) and guards on the same set (M12). So a stored name and an API-normalised name agree on surrounding whitespace.
- **Reads** (`GET /Person`, `GET /Person/{name}`): the `[RegularExpression]` runs on the raw value before `Normalize`. So non-ASCII whitespace at the edges of a query value gives 400 rather than being trimmed. This is unchanged from contract 001.

`ValidNameAttribute` (`exercise1/api/Business/Validation/ValidNameAttribute.cs`), `[AttributeUsage(Property | Parameter | Field)]`, sealed:
- `null` -> valid (presence is `[Required]`'s job).
- not a `string` -> invalid.
- (Amendment 1b) a `string` longer than `InputRules.MaxRawLength` (800) -> invalid, without normalising (R23).
- otherwise valid iff `InputRules.IsValidName(InputRules.Normalize(value))`. For example, 100 letters written decomposed (200 raw characters) are valid; 101 are not.
- `ErrorMessage` = `InputRules.AllowedCharactersMessage` (never includes the value).

Usage:
- `PUT /Person/{personId}` body: `[Required, ValidName]` on `UpdatePersonRequest.Name`. Task 006 uses the same pair.
- `GET /Person` and `GET /Person/{name}` keep `[StringLength(InputRules.MaxLength[, MinimumLength = 1])]` and `[RegularExpression(InputRules.AllowedCharactersPattern, ErrorMessage = ...)]` unchanged in code; they get the new pattern through the constant.
- `GetPeopleHandler.Normalize` becomes `string.IsNullOrWhiteSpace(value) ? null : InputRules.Normalize(value)`; `GetPersonByNameHandler` sends `InputRules.Normalize(request.Name)` instead of `request.Name.Trim()`. SQL unchanged.

## Request, handler and result (`exercise1/api/Business/Commands/UpdatePerson.cs`)

```csharp
public sealed class UpdatePersonRequest            // [FromBody]
{
    [Required, ValidName]
    public string? Name { get; init; }
}

public sealed class UpdatePerson : IRequest<UpdatePersonResult>
{
    public required int PersonId { get; init; }
    public required string Name { get; init; }      // raw; the handler normalises
}

public sealed class UpdatePersonResult : BaseResponse
{
    public PersonAstronaut? Person { get; set; }
}
```

The controller passes `Name = request.Name ?? string.Empty` (never `!`; `[Required]` already rejected null).

`UpdatePersonHandler(StargateContext context, ILogger<UpdatePersonHandler> logger)`, `Handle(request, ct)`:
1. (Amendment 1b) `var name = request.Name.Length <= InputRules.MaxRawLength ? InputRules.Normalize(request.Name) : string.Empty;` if `!InputRules.IsValidName(name)` throw `ArgumentException("Invalid name.", nameof(request))` (unreachable over HTTP; never includes the value; a raw value over 800 characters is never normalised, H18).
2. `var person = await context.People.SingleOrDefaultAsync(p => p.Id == request.PersonId, ct);` null -> return the **404 result** `{ Success = false, Message = "Person not found", ResponseCode = 404, Person = null }`.
3. `if (await context.People.AnyAsync(p => p.Id != request.PersonId && EF.Functions.Collate(p.Name, "NOCASE") == name, ct))` -> return `{ Success = false, Message = "A person with this name already exists", ResponseCode = 409, Person = null }`.
4. `person.Name = name;` then `try { await context.SaveChangesAsync(ct); }`
   - (Amendment 1b) `catch (DbUpdateConcurrencyException)` -> the **404 result**. The row was deleted between the load and the save, so the UPDATE matched no row (H16). This catch comes first, because `DbUpdateConcurrencyException` derives from `DbUpdateException`.
   - `catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))` -> the same 409 result.
5. Read back with Dapper, parameterised, static SQL:
   ```sql
   SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
   FROM [Person] a
   LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
   WHERE a.Id = @PersonId
   ```
   If a row comes back, return `{ Person = <row> }` (defaults: Success true, "Successful", 200).
   (Amendment 1) If no row comes back, return the **404 result**. The person was deleted between the save and the read-back (H17). A 200 with `Person = null` is never returned.
6. Logging: `LogInformation("Renamed person {PersonId}", id)` on 200; `LogInformation("Rename of person {PersonId} rejected: {Reason}", id, "not found" | "name in use")` on every 404/409. Never log names.

No delete endpoint exists yet, so both deletion races (steps 4 and 5) need an out-of-band delete today. Tests drive them with interceptors.

No explicit transaction: one row, one statement; the unique index is the guarantee. `AstronautDetail` and `AstronautDuty` are never queried through EF or written.

`exercise1/api/Business/Data/SqliteErrors.cs`:

```csharp
/// SQLite-specific and transitional (like StarbaseDatabase): replace when the project moves to SQL Server.
public static class SqliteErrors
{
    public const int UniqueConstraintExtendedCode = 2067; // SQLITE_CONSTRAINT_UNIQUE
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: UniqueConstraintExtendedCode };
}
```

## Controller (`PersonController`)

```csharp
[HttpPut("{personId}")]
[Consumes("application/json")]
[RequestSizeLimit(4096)]                    // Amendment 1b
[EndpointSummary("Rename a person")]
[EndpointDescription("...as openapi.yaml UpdatePerson.description...")]
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
```

No route constraint (`{personId:int}` would make `PUT /Person/abc` a 405 via the `GET /Person/{name}` path match). No try/catch. Existing actions unchanged.

**Request size (Amendment 1b).** `[RequestSizeLimit(4096)]`: on Kestrel, a request body over 4096 bytes is refused with **413** before model binding. Nothing changes and the body is not echoed. The 413 body is not specified, and clients key on the status. The in-memory TestServer does not enforce this limit, so P17 accepts 413 or 400 (the 400 comes from the 800-character raw bound). The 413 itself is a QA check against real Kestrel. 413 is not in `ProducesResponseType`, so Swagger does not list it.

Every valid name fits comfortably. At most 100 letters with 3 marks each is 400 raw characters, which is 2400 bytes even if every character is JSON-escaped as six characters.

**Validation order (Amendment 1b).** Model binding and validation (route `personId`, then body) run before the handler. So:
- `PUT /Person/999999` with an invalid name gives **400** (key `Name`), not 404 (P18a).
- `PUT /Person/0` with a valid name gives **400** (key `personId`, from `[Range]`), not 404 (P18b).
- 404 and 409 are only possible once both inputs are valid.

**415 bodies (Amendment 1).** `[Consumes("application/json")]` behaves differently depending on the request's Content-Type. The `ProducesResponseType(typeof(ProblemDetails), 415)` attribute only documents the second case.

| Request | Rejected by | Response |
|---|---|---|
| Content-Type present and not JSON (`text/plain`, `application/xml`, ...) | Endpoint routing (consumes matcher) | 415, **empty body**, no `Content-Type` |
| No Content-Type header, with a body | MVC body binding (`[ApiController]` client-error mapping) | 415, `application/problem+json` `ProblemDetails` (`status` 415) |

The contract guarantees only the 415 status. Clients and tests must not read the body.

**JSON property order (Amendment 1).** System.Text.Json writes the derived type's properties first. So the 200/404/409 body comes out as `person, success, message, responseCode`. Property order is not part of the contract, and clients must not depend on it.

## Program.cs

```csharp
builder.Services.AddControllers(options =>
{
    // Binding errors on route/query values must not echo the submitted text.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((_, _) => "The value is not valid.");
    messages.SetValueIsInvalidAccessor(_ => "The value is not valid.");
    messages.SetValueMustBeANumberAccessor(_ => "The value must be a number.");
    // Amendment 1: parameters (a route value such as personId) use the "non-property" variants, which are the ones that echo.
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "The value is not valid.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "The value must be a number.");
});
```

Revision 1 set only the first three accessors. Those are the property variants. A failed conversion of the `personId` action parameter uses the non-property variant, so revision 1 would still have returned `The value '99999999999' is not valid.`. Requirement P8 ("no echo") needs all five.

Resulting `errors` entries for `PUT /Person/{personId}` (key `personId`):

| Route value | Cause | `errors.personId` |
|---|---|---|
| `abc`, `notanumber`, `1.5`, `99999999999` (Int32 overflow) | Binding fails (format or overflow) | `["The value is not valid."]` |
| `0`, `-1` | `[Range(1, int.MaxValue)]` | `["The field personId must be between 1 and 2147483647."]` (framework default; does not contain the submitted value) |

Example 400 body for `PUT /Person/abc` (`traceId` varies):
`{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"personId":["The value is not valid."]},"traceId":"..."}`

The message strings are informative. Clients must not parse them, and tests assert only the key and the absence of the submitted value. No existing route or query parameter is non-string, so no existing response changes.

## How each domain rule is enforced

| Rule | Code | Database |
|---|---|---|
| 1 Name unique | `InputRules.Normalize` (NFC + trim) before validate/compare/store; NOCASE pre-check excluding own id | `COLLATE NOCASE` column + unique `IX_Person_Name`; violation (2067) mapped to 409; migration guard + trim on the same whitespace set as the API |
| 2 No assignment -> no astronaut rows | Rename writes only `Person.Name` | FKs unchanged; rebuild keeps Ids |
| 3-7 Duties | Not touched | Not touched |

Known limits (documented, accepted): case folding is ASCII-only; stored non-NFC names from before this task are not rewritten; until task 006, `POST /Person` neither trims nor NFC-normalises, so a padded name can be inserted beside its trimmed form, and a case-variant duplicate POST returns the controller's generic 500.
