# Task 007: data contract (revision 1, Amendment 1)

Markdown because agents read it as text. Baseline: `main` at `280fcd9`.

> **Amendment 1** (after implementation, merged at `81f13f0`; documents the merged behaviour, no new work):
> - Section 2 `PersonSql` and section 3 step 2: person resolution is **exact-case match first, then lowest Id**
>   (was: lowest Id only). The create handler, `GetAstronautDutiesByName` and `GetPersonByName` now pick the same
>   person. This is observable only on a database that predates `AddUniquePersonNameIndex`; see section 1a.
> - Section 1: `Person.Name` has collation NOCASE and `IX_Person_Name` exists only after `AddUniquePersonNameIndex`;
>   the explicit `COLLATE NOCASE` in the lookups is what keeps them case-insensitive before that.
> - Section 5: rule 1 wording updated; the date-offset statement corrected (an explicit numeric offset is
>   converted to server local time; `Z` is not); a "Known, deliberately out of scope" list and a deployment
>   follow-up added (section 6).

## 1. Schema: no change

No migration, no new index, no new package. Existing objects this task relies on (from the model snapshot):

| Object | Definition | Used for |
|---|---|---|
| `Person.Name` | TEXT, collation `NOCASE` (since `AddUniquePersonNameIndex`; BINARY before it) | case-insensitive exact lookup (rule 1) |
| `IX_Person_Name` | UNIQUE on `Person(Name)` (inherits NOCASE; created by `AddUniquePersonNameIndex`) | at most one person per name ignoring ASCII case |
| `IX_AstronautDetail_PersonId` | UNIQUE on `AstronautDetail(PersonId)` | one detail per person; the only unique index the duty create can hit (concurrent first duties) and the only one mapped to 409 here |
| `IX_AstronautDuty_PersonId` | non-unique on `AstronautDuty(PersonId)` | duty lookups by person |
| FKs | `AstronautDetail.PersonId`, `AstronautDuty.PersonId` -> `Person.Id` | unchanged |

Dates are stored by EF Core SQLite as TEXT `yyyy-MM-dd HH:mm:ss` (fraction only when non-zero). Every duty and career
date this API writes is midnight (`.Date`), so lexical order equals chronological order; the 1900-2100 bound keeps
four-digit years.

**Not added here (task 009 owns them):** a partial unique index on `AstronautDuty(PersonId) WHERE DutyEndDate IS NULL`,
a unique index on `AstronautDuty(PersonId, DutyStartDate)`, and the transaction design. The duplicate rule of this task
is therefore enforced by the handler pre-check only; a concurrent identical POST can still create two rows (known, 009).

### 1a. Person resolution (Amendment 1)

All three name lookups (create handler, `GetAstronautDutiesByName`, `GetPersonByName`) resolve a name identically:

1. trimmed of ASCII whitespace (`InputRules.Normalize`);
2. filtered by an explicit case-insensitive comparison (`COLLATE NOCASE` / `EF.Functions.Collate(..., "NOCASE")`);
3. ordered by "stored name equals the input exactly, byte for byte" first, then by `Id` ascending; first row wins.

On a migrated database (`IX_Person_Name` present) step 2 can match at most one row, so step 3 never decides anything,
and the explicit `COLLATE NOCASE` is redundant with the column's collation. Both matter only on a database that
predates `AddUniquePersonNameIndex`, where `Person.Name` is BINARY and `bob` (Id 4) and `Bob` (Id 9) can coexist:
`Bob` resolves to 9, `bob` to 4, `BOB` (neither exact) to 4, in every lookup. Such a database can be served only
outside Development (Development migrates at startup, and that migration refuses case-colliding names).

## 2. GET /AstronautDuty?name= (GetAstronautDutiesByNameHandler)

Dapper, two static `const string` raw literals (never `$`), parameters via `new CommandDefinition(sql, params, cancellationToken: ct)`.

```sql
-- PersonSql. @Name = InputRules.Normalize(request.Name)
SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
FROM [Person] a
LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
WHERE a.Name = @Name COLLATE NOCASE
ORDER BY CASE WHEN a.Name = @Name COLLATE BINARY THEN 0 ELSE 1 END, a.Id
LIMIT 1
```

```sql
-- DutiesSql. @PersonId = the PersonId returned by PersonSql (never anything from the request)
SELECT Id, PersonId, Rank, DutyTitle, DutyStartDate, DutyEndDate
FROM [AstronautDuty]
WHERE PersonId = @PersonId
ORDER BY DutyStartDate DESC, Id DESC
```

- PersonSql maps to `PersonAstronaut` (unchanged; LEFT JOIN nulls give `""` strings and null dates, as today).
- DutiesSql maps to the new `Business/Dtos/AstronautDutyItem` (`int Id`, `int PersonId`, `string Rank`,
  `string DutyTitle`, `DateTime DutyStartDate`, `DateTime? DutyEndDate`; no navigation property).
- No person row: return `{ Person = null, AstronautDuties = [], Success = false, Message = "Person not found", ResponseCode = 404 }`.
  DutiesSql is not run.
- Both reads are outside a transaction; a duty added between them may or may not appear (read skew on a GET is accepted).
- The person SQL is a copy, not a call to `GetPersonByName` (task 008 deletes it). `AstronautDutyController` must not
  reference `GetPersonByName`, `GetPersonByNameResult` or `PersonController`. `GetPersonByName`'s own SQL writes the
  CASE without `COLLATE BINARY`; that is equivalent (BINARY column before the index migration, at most one match after).
- A repeated query parameter (`?name=a&name=b`) reaches the handler as the first value only (MVC binding of a
  single-valued `string?`); the handler never sees both.

## 3. POST /AstronautDuty (CreateAstronautDutyHandler)

EF Core LINQ only (no Dapper, no raw SQL) so every value is a parameter. Constructor: `(StargateContext context,
ILogger<CreateAstronautDutyHandler> logger)`. `CreateAstronautDutyPreProcessor` and its `Program.cs` registration are deleted.

Algorithm (one request = one person resolution):

1. `name = InputRules.Normalize(request.Name)`, `rank = InputRules.Normalize(request.Rank)`,
   `title = InputRules.Normalize(request.DutyTitle)`, `start = request.DutyStartDate.Date`.
   If any of `IsValidName(name|rank|title)` or `IsValidDutyDate(request.DutyStartDate)` is false: throw
   `ArgumentException("Invalid duty request.", nameof(request))` (unreachable over HTTP; no value in the message).
2. Resolve the person ONCE (section 1a):
   ```csharp
   var person = await _context.People.AsNoTracking()
       .Where(p => EF.Functions.Collate(p.Name, "NOCASE") == name)
       .OrderBy(p => EF.Functions.Collate(p.Name, "BINARY") == name ? 0 : 1)
       .ThenBy(p => p.Id)
       .Select(p => new { p.Id, p.Name })
       .FirstOrDefaultAsync(ct);
   ```
   `null` -> 404 result (`Id = null, Name = null, Success = false, Message = "Person not found", ResponseCode = 404`).
   From here on only `person.Id` (and `person.Name` for the Location) is used.
3. Duplicate pre-check, scoped to the person, date part on both sides, title ignoring ASCII case:
   ```csharp
   var duplicate = await _context.AstronautDuties.AsNoTracking().AnyAsync(d =>
       d.PersonId == person.Id
       && EF.Functions.Collate(d.DutyTitle, "NOCASE") == title
       && d.DutyStartDate.Date == start, ct);
   ```
   `true` -> 409 with message `"A duty with this title and start date already exists for this person"`.
4. Rules 3-7, translated line for line from `main` (behaviour unchanged for the same valid, unpadded input):
   ```csharp
   var detail = await _context.AstronautDetails.FirstOrDefaultAsync(d => d.PersonId == person.Id, ct);   // tracked
   if (detail is null)
   {
       detail = new AstronautDetail { PersonId = person.Id, CurrentDutyTitle = title, CurrentRank = rank, CareerStartDate = start };
       if (title == "RETIRED") detail.CareerEndDate = start;            // known defect, task 009 (rule 7 says start - 1)
       _context.AstronautDetails.Add(detail);
   }
   else
   {
       detail.CurrentDutyTitle = title;
       detail.CurrentRank = rank;
       if (title == "RETIRED") detail.CareerEndDate = start.AddDays(-1); // ordinal, case-sensitive: task 009
   }

   var latest = await _context.AstronautDuties
       .Where(d => d.PersonId == person.Id)
       .OrderByDescending(d => d.DutyStartDate).ThenByDescending(d => d.Id)   // tie-break: new, deterministic
       .FirstOrDefaultAsync(ct);                                                // tracked
   if (latest is not null) latest.DutyEndDate = start.AddDays(-1);             // even if already closed: task 009

   var duty = new AstronautDuty { PersonId = person.Id, Rank = rank, DutyTitle = title, DutyStartDate = start, DutyEndDate = null };
   _context.AstronautDuties.Add(duty);
   ```
5. One `await _context.SaveChangesAsync(ct)` (EF wraps it in a transaction; all rows or none; a token cancelled
   during the save rolls everything back).
   `catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))` -> 409 with message
   `"The person's duties were changed by another request"` (only `IX_AstronautDetail_PersonId` can fire).
   Any other exception propagates (generic 500, no body text in Production).
6. Log `"Created duty {DutyId} for person {PersonId}"` (Information). Rejections log
   `"Create duty rejected: {Reason}"` (person not found) or `"Create duty rejected: {Reason} for person {PersonId}"`
   (`"duplicate"`, `"concurrent change"`). Never log name, rank, title or the body.
7. Return `{ Id = duty.Id, Name = person.Name, ResponseCode = 201 }` (`Success`/`Message` defaults). `Name` is
   `[JsonIgnore]`; the controller builds `Location: "/AstronautDuty?name=" + Uri.EscapeDataString(result.Name)`.

Equivalence with `main` for valid unpadded input: `(x.AddDays(-1)).Date == x.Date.AddDays(-1)`; tracked in-place
updates write the same values that `Update()` on a Dapper-read entity wrote (EF now writes only changed columns; the
unchanged columns keep identical values). Two deliberate differences, both called out in the plan: (a) rank and title
are trimmed before storage; (b) the `Id DESC` tie-break for equal latest start dates (undefined on `main`).

Transaction around read-decide-write: **not added** (plan section 3.4); task 009.

## 4. Validation (before any SQL)

| Input | Attributes | Rule |
|---|---|---|
| GET `name` (query parameter, `string?`) | `[FromQuery, Required, ValidName]` | NameRule after ASCII trim. Missing, `?name`, empty or whitespace-only (space, tab, LF) bind to null: only the Required message (Amendment 1). Repeated: first value. |
| body `name`, `rank`, `dutyTitle` (`string?`) | `[Required, ValidName]` | NameRule after ASCII trim; stored trimmed (rank, title). Empty or whitespace-only: Required then NameRule message. |
| body `dutyStartDate` (`DateTime?`) | `[Required, ValidDutyDate]` | date part in 1900-01-01..2100-12-31 inclusive |

New in `InputRules`: `public static readonly DateTime MinDutyDate = new(1900, 1, 1)`, `MaxDutyDate = new(2100, 12, 31)`,
`public static bool IsValidDutyDate(DateTime value) => value.Date >= MinDutyDate && value.Date <= MaxDutyDate;`,
`public const string DutyDateMessage = "Use a date between 1900-01-01 and 2100-12-31.";`.
New `ValidDutyDateAttribute` (sealed, `AttributeTargets.Property | Parameter | Field`, message from `DutyDateMessage`,
null is valid, a non-`DateTime` value is invalid, value never echoed).

`CreateAstronautDutyRequest` (sealed): `public string? Name { get; init; }`, `Rank`, `DutyTitle`,
`public DateTime? DutyStartDate { get; init; }`; no `required` keyword (it would turn a missing member into a parser
error instead of a field message).

## 5. Domain rules

| Rule | Enforcement in this task |
|---|---|
| 1 | One parameterised, explicitly NOCASE, exact lookup per request (both handlers), with the same tie-break as `GetPersonByName` (section 1a), plus `IX_Person_Name` (unique, NOCASE) on a migrated database. A name can never widen the match. |
| 2 | GET returns `[]` for a person without duties; 400/404/409 write nothing (dump-compared in tests). |
| 3-7 | Unchanged (section 3, step 4). Known defects are listed in section 6. |
| "No interpolated SQL, ever" | No `$`-string, concatenation, `FromSqlRaw`, `FromSqlInterpolated` or `ExecuteSql*` in either handler or anything they call. |

## 6. Known, deliberately out of scope (Amendment 1)

**Task 009 (rules and concurrency):**
- `dutyStartDate` with an explicit numeric UTC offset (for example `-05:00`, and also `+00:00`) is converted by
  System.Text.Json to the server's local time before `.Date`, so the stored date can be a day earlier or later
  depending on the server's time zone (verified by probe in UTC, America/Chicago and Asia/Tokyo). A value ending in
  `Z` is NOT converted (it keeps its written date in every zone). A date-only value is unaffected. The 1900-2100
  bounds are still enforced on the date the server ends up with.
- No transaction around read-decide-write and no partial unique index for the open duty (or for
  `(PersonId, DutyStartDate)`): two concurrent POSTs for a person who already has a duty can both succeed and
  leave two open duties (or two identical duties). Only the first-duty race (`IX_AstronautDetail_PersonId`) maps to 409.
- Rules 3-7 defects: a first `RETIRED` duty sets `CareerEndDate = start` (rule 7 says start minus 1); `RETIRED` is
  matched case-sensitively; back-dated and same-day starts are accepted; an already-closed latest duty is closed
  again; a later non-retired duty does not clear `CareerEndDate`.

**Deployment follow-up (not this task, no task yet):** outside Development the API starts and serves requests on a
database with pending migrations (this is how a pre-index database, section 1a, can be reached at all). Non-Development
environments should refuse to start while migrations are pending.
