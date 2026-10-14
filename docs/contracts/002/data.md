# Task 002: data contract (revision 1)

Markdown because agents read and edit it as text. Human summary: `contract.html`.

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

Then prepend, at the top of the generated `Up`, two `migrationBuilder.Sql` calls, in this order, before the generated `AlterColumn` and `CreateIndex`:

```csharp
// Rule 1 (task 002): abort, changing nothing, if names collide ignoring ASCII case and surrounding whitespace.
// Must run before EF's table rebuild, which commits around PRAGMA foreign_keys: EF also creates the unique index on the
// old BINARY column first and the NOCASE index only after the committed rebuild, so without this guard case-only
// duplicates would fail half-way. SQLite-specific and transitional (same pattern as MakeCareerStartDateRequired).
migrationBuilder.Sql(
    """
    CREATE TEMP TABLE "__guard_PersonName" ("x" INTEGER);
    CREATE TEMP TRIGGER "__guard_PersonName_check" BEFORE INSERT ON "__guard_PersonName"
    WHEN EXISTS (SELECT 1 FROM main."Person" GROUP BY trim("Name", char(32, 9, 10, 13)) COLLATE NOCASE HAVING COUNT(*) > 1)
    BEGIN
        SELECT RAISE(ABORT, 'AddUniquePersonNameIndex: some Person names are equal ignoring ASCII case and leading or trailing whitespace, so the unique index on Person.Name cannot be created. No data was changed. List them with: SELECT trim(Name, char(32,9,10,13)) COLLATE NOCASE AS n, group_concat(Id) FROM Person GROUP BY n HAVING COUNT(*) > 1; then rename or merge those people (back up the database before changing data). Only for a disposable development database: delete starbase.db and restart.');
    END;
    INSERT INTO "__guard_PersonName" ("x") VALUES (1);
    DROP TABLE "__guard_PersonName";
    """);

// Names are trimmed before they are stored (task 002 decision). Safe: the guard proved no collision after trimming.
migrationBuilder.Sql("""UPDATE "Person" SET "Name" = trim("Name", char(32, 9, 10, 13)) WHERE "Name" <> trim("Name", char(32, 9, 10, 13));""");
```

Requirements on the message (tests assert these substrings): `AddUniquePersonNameIndex`, `No data was changed`, `back up`. It must contain no single quote other than the delimiters (SQL string literal).

`Down`: exactly as generated (`DropIndex IX_Person_Name`, `AlterColumn` removing the collation). It does not un-trim names.

Expected effects (verified by probe on SQLite with EF 10.0.13):
- Duplicates (`Ada`, `"  ADA "`): `SqliteException` (wrapped by EF) with the message above; `__EFMigrationsHistory`, all rows (still untrimmed), the `Person` DDL and indexes unchanged; no `ef_temp_%` or `__guard_%` objects left.
- No duplicates: names trimmed; `Person` DDL contains `"Name" TEXT COLLATE NOCASE NOT NULL`; `IX_Person_Name` unique; `pragma_index_xinfo('IX_Person_Name')` reports `coll = 'NOCASE'` for `Name`; Ids and all `AstronautDetail`/`AstronautDuty` rows unchanged.
- EF logs a warning that the rebuild "cannot be executed in a transaction" (same as task 003). Expected; not an error.

Existing rows are not NFC-normalised (not possible in SQLite SQL). Documented residual risk.

## Shared name rule (`exercise1/api/Business/Validation/InputRules.cs`)

```csharp
public static partial class InputRules   // partial only if [GeneratedRegex] is used
{
    public const int MaxLength = 100;

    // Default-ignorable code points that Unicode classes as letters (Lo) or nonspacing marks (Mn): they render as nothing.
    private const string Invisible = @"͏ᅟᅠ឴឵᠋-᠏ㅤ︀-️ﾠ";

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

`ValidNameAttribute` (`exercise1/api/Business/Validation/ValidNameAttribute.cs`), `[AttributeUsage(Property | Parameter | Field)]`, sealed:
- `null` -> valid (presence is `[Required]`'s job).
- not a `string` -> invalid.
- otherwise valid iff `InputRules.IsValidName(InputRules.Normalize(value))`.
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
1. `var name = InputRules.Normalize(request.Name);` if `!InputRules.IsValidName(name)` throw `ArgumentException("Invalid name.", nameof(request))` (unreachable over HTTP; never includes the value).
2. `var person = await context.People.SingleOrDefaultAsync(p => p.Id == request.PersonId, ct);` null -> return `{ Success = false, Message = "Person not found", ResponseCode = 404, Person = null }`.
3. `if (await context.People.AnyAsync(p => p.Id != request.PersonId && EF.Functions.Collate(p.Name, "NOCASE") == name, ct))` -> return `{ Success = false, Message = "A person with this name already exists", ResponseCode = 409, Person = null }`.
4. `person.Name = name;` then `try { await context.SaveChangesAsync(ct); } catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex)) { return <the same 409 result>; }`.
5. Read back with Dapper, parameterised, static SQL:
   ```sql
   SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
   FROM [Person] a
   LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
   WHERE a.Id = @PersonId
   ```
   Return `{ Person = <row> }` (defaults: Success true, "Successful", 200).
6. Logging: `LogInformation("Renamed person {PersonId}", id)` on 200; `LogInformation("Rename of person {PersonId} rejected: {Reason}", id, "not found" | "name in use")` on 404/409. Never log names.

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

## Program.cs

```csharp
builder.Services.AddControllers(options =>
{
    // Binding errors on route/query values must not echo the submitted text.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((_, _) => "The value is not valid.");
    messages.SetValueIsInvalidAccessor(_ => "The value is not valid.");
    messages.SetValueMustBeANumberAccessor(_ => "The value must be a number.");
});
```

## How each domain rule is enforced

| Rule | Code | Database |
|---|---|---|
| 1 Name unique | `InputRules.Normalize` (NFC + trim) before validate/compare/store; NOCASE pre-check excluding own id | `COLLATE NOCASE` column + unique `IX_Person_Name`; violation (2067) mapped to 409; migration guard + trim |
| 2 No assignment -> no astronaut rows | Rename writes only `Person.Name` | FKs unchanged; rebuild keeps Ids |
| 3-7 Duties | Not touched | Not touched |

Known limits (documented, accepted): case folding is ASCII-only; stored non-NFC names from before this task are not rewritten; until task 006, `POST /Person` neither trims nor NFC-normalises, so a padded name can be inserted beside its trimmed form, and a case-variant duplicate POST returns the controller's generic 500.
