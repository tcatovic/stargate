# Task 007: data contract (revision 1)

Markdown because agents read it as text. Baseline: `main` at `280fcd9`.

## 1. Schema: no change

No migration, no new index, no new package. Existing objects this task relies on (from the model snapshot):

| Object | Definition | Used for |
|---|---|---|
| `Person.Name` | TEXT, collation `NOCASE` | case-insensitive exact lookup (rule 1) |
| `IX_Person_Name` | UNIQUE on `Person(Name)` (inherits NOCASE) | at most one person per name ignoring ASCII case |
| `IX_AstronautDetail_PersonId` | UNIQUE on `AstronautDetail(PersonId)` | one detail per person; the only unique index the duty create can hit (concurrent first duties) and the only one mapped to 409 here |
| `IX_AstronautDuty_PersonId` | non-unique on `AstronautDuty(PersonId)` | duty lookups by person |
| FKs | `AstronautDetail.PersonId`, `AstronautDuty.PersonId` -> `Person.Id` | unchanged |

Dates are stored by EF Core SQLite as TEXT `yyyy-MM-dd HH:mm:ss` (fraction only when non-zero). Every duty and career
date this API writes is midnight (`.Date`), so lexical order equals chronological order; the 1900-2100 bound keeps
four-digit years.

**Not added here (task 009 owns them):** a partial unique index on `AstronautDuty(PersonId) WHERE DutyEndDate IS NULL`,
a unique index on `AstronautDuty(PersonId, DutyStartDate)`, and the transaction design. The duplicate rule of this task
is therefore enforced by the handler pre-check only; a concurrent identical POST can still create two rows (known, 009).

## 2. GET /AstronautDuty?name= (GetAstronautDutiesByNameHandler)

Dapper, two static `const string` raw literals (never `$`), parameters via `new CommandDefinition(sql, params, cancellationToken: ct)`.

```sql
-- PersonSql. @Name = InputRules.Normalize(request.Name)
SELECT a.Id AS PersonId, a.Name, b.CurrentRank, b.CurrentDutyTitle, b.CareerStartDate, b.CareerEndDate
FROM [Person] a
LEFT JOIN [AstronautDetail] b ON b.PersonId = a.Id
WHERE a.Name = @Name COLLATE NOCASE
ORDER BY a.Id
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
  reference `GetPersonByName`, `GetPersonByNameResult` or `PersonController`.

## 3. POST /AstronautDuty (CreateAstronautDutyHandler)

EF Core LINQ only (no Dapper, no raw SQL) so every value is a parameter. Constructor: `(StargateContext context,
ILogger<CreateAstronautDutyHandler> logger)`. `CreateAstronautDutyPreProcessor` and its `Program.cs` registration are deleted.

Algorithm (one request = one person resolution):

1. `name = InputRules.Normalize(request.Name)`, `rank = InputRules.Normalize(request.Rank)`,
   `title = InputRules.Normalize(request.DutyTitle)`, `start = request.DutyStartDate.Date`.
   If any of `IsValidName(name|rank|title)` or `IsValidDutyDate(request.DutyStartDate)` is false: throw
   `ArgumentException("Invalid duty request.", nameof(request))` (unreachable over HTTP; no value in the message).
2. Resolve the person ONCE:
   ```csharp
   var person = await _context.People.AsNoTracking()
       .Where(p => EF.Functions.Collate(p.Name, "NOCASE") == name)
       .OrderBy(p => p.Id)
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
5. One `await _context.SaveChangesAsync(ct)` (EF wraps it in a transaction; all rows or none).
   `catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))` -> 409 with message
   `"The person's duties were changed by another request"` (only `IX_AstronautDetail_PersonId` can fire).
   Any other exception propagates (generic 500, no body text in Production).
6. Log `"Created duty {DutyId} for person {PersonId}"` (Information). Rejections log
   `"Create duty rejected: {Reason}"` plus `{PersonId}` when known (`"person not found"`, `"duplicate"`, `"concurrent change"`).
   Never log name, rank, title or the body.
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
| GET `name` (query parameter, `string?`) | `[FromQuery, Required, ValidName]` | NameRule after ASCII trim |
| body `name`, `rank`, `dutyTitle` (`string?`) | `[Required, ValidName]` | NameRule after ASCII trim; stored trimmed (rank, title) |
| body `dutyStartDate` (`DateTime?`) | `[Required, ValidDutyDate]` | date part in 1900-01-01..2100-12-31 inclusive |

New in `InputRules`: `public static readonly DateTime MinDutyDate = new(1900, 1, 1)`, `MaxDutyDate = new(2100, 12, 31)`,
`public static bool IsValidDutyDate(DateTime value) => value.Date >= MinDutyDate && value.Date <= MaxDutyDate;`,
`public const string DutyDateMessage = "Use a date between 1900-01-01 and 2100-12-31.";`.
New `ValidDutyDateAttribute` (sealed, `AttributeTargets.Property | Parameter | Field`, message from `DutyDateMessage`,
null is valid, value never echoed).

`CreateAstronautDutyRequest` (sealed): `public string? Name { get; init; }`, `Rank`, `DutyTitle`,
`public DateTime? DutyStartDate { get; init; }`; no `required` keyword (it would turn a missing member into a parser
error instead of a field message).

## 5. Domain rules

| Rule | Enforcement in this task |
|---|---|
| 1 | One parameterised NOCASE exact lookup per request (both handlers) + `IX_Person_Name` (unique, NOCASE). A name can never widen the match. |
| 2 | GET returns `[]` for a person without duties; 400/404/409 write nothing (dump-compared in tests). |
| 3-7 | Unchanged (section 3, step 4). Known defects left for task 009: first-RETIRED `CareerEndDate = start`; case-sensitive `RETIRED`; back-dated/same-day starts accepted and an already-closed latest duty re-closed; no transaction; no open-duty or `(PersonId, DutyStartDate)` unique index; `CareerEndDate` not cleared by a later duty; offset date-times converted to server local time before `.Date`. |
| "No interpolated SQL, ever" | No `$`-string, concatenation, `FromSqlRaw`, `FromSqlInterpolated` or `ExecuteSql*` in either handler or anything they call. |
