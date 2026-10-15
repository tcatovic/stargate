# Task 007: tests each platform must satisfy (revision 1, Amendment 1)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted
state), not implementation. IDs are unique within this file and are the prefixes of the test method names in
`exercise1/api.Tests` (for example `G7b_...`). Literals (messages) are written out in tests, not taken from the
production constants (existing `ProblemAssert` style).

> **Amendment 1** (after implementation, merged at `81f13f0`). This file now lists the merged suite exactly.
> - Corrected: **G7** (whitespace-only GET name gives only the Required message, not `[Required, NameRule]`);
>   **A15** (the test uses Elena and a padded `RETIRED`); **A16** (Kestrel, Development, a dropped `AstronautDuty`
>   table, not an unmigrated database; GET and POST); **K2** (asserts an empty body, split into K2 GET and K2b POST);
>   **G8** (the 100-character case moved to G8b; the four S4 payloads added); **G6** (also `?name` without `=`).
> - Added after the plan: G7b, G7c, G8b, A3b, A3c, A9b, A9c, A11b, A11c, A17, S6b, H7, H8, CH12b, CH15, CH16, CH17,
>   U4, K2b, V1b, and section 5a (`PersonResolutionPreIndexHttpTests`).
> - Removed (as planned): `NullPersonGuardTests.cs` and its two `PreProcessor_*` tests no longer exist.
> - Section 6 (mutation map) corrected: dropping the explicit `COLLATE NOCASE` is an equivalent mutation on a
>   migrated database, so G2/A2/H4/S7 do not catch it; H8 and CH17 (pre-index schema) do.

Fixture conventions (API):
- HTTP tests: `DutyApiHost` (`WebApplicationFactory<Program>`, Development by default, `AllowAutoRedirect = false`)
  over a per-test temp SQLite file from `TestDatabase.Seeded()` (migrated, `DevelopmentSeedData.People()`; ids: Mira 1,
  Kenji 2, Tomas Lindqvist 3, Priya 4, Elena 5, Desmond 6). Hostile tests first assert `Person.Id 3` is `Tomas Lindqvist`.
  `DutyApiHost(seed: false, migrateTo: <migration>, environment: "Production")` gives an older schema that startup
  does not migrate forward.
- Test helper `TestDatabase.DumpExcept(long personId)`: like `Dump()` but excludes `Person` rows with
  `Id = personId` and `AstronautDetail`/`AstronautDuty` rows with `PersonId = personId` (SQLite parameter).
  "Others unchanged" always means `DumpExcept(target)` before == after.
- "Nothing written" means `Dump()` before == after.
- "No echo" means `ProblemAssert.NothingDecodedContains(json, submitted...)` and, for envelopes, the raw body does not
  contain the submitted value.
- Messages: NotFound = `Person not found`; Dup = `A duty with this title and start date already exists for this person`;
  Race = `The person's duties were changed by another request`; DateMsg = `Use a date between 1900-01-01 and 2100-12-31.`;
  RequiredName = `The name field is required.`; NameRule = the existing `ProblemAssert.NameRuleMessage`.

## 1. Security: hostile matrix over HTTP (`AstronautDutySecurityHttpTests`; S2-S4 live in `AstronautDutyHttpTests`)

These fail on `main` 280fcd9 (write tests: another person's rows change or a 500; read tests: route missing).

**S1 (Theory) Write path, a valid-rule payload is just a name.** Rows:
`a' OR Id IS 3 OR 'a`, `a' OR 1 OR 'a`, `a' OR 'a' IS 'a`, `Tomas Lindqvist'--`, `Aoife O'Brien Jr.`, `O'Neil-Smith Jr.`.
- Given the seeded DB, When `POST /Person {name}`, Then 201 and the new id `T` (= 7).
- Given `before = DumpExcept(T)`, When `POST /AstronautDuty {name, rank:"CPT", dutyTitle:"Injected Role", dutyStartDate:"2026-03-01"}`,
  Then 201, `Location` = `/AstronautDuty?name=` + `Uri.EscapeDataString(name)`, exactly one `AstronautDuty` row with
  `PersonId = T` (CPT, Injected Role, 2026-03-01, end null), one `AstronautDetail` with `PersonId = T`
  (CPT, Injected Role, career start 2026-03-01, end null), `DumpExcept(T)` == `before`, and Tomas's Pilot duty still
  has `DutyEndDate` null.

**S2 Read path, own duties only.** Given the person `a' OR Id IS 3 OR 'a` (Id 7) with one duty inserted directly, When
`GET /AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a`, Then 200, `person.personId == 7`,
`person.name` is the payload, `astronautDuties` is exactly [Injected Role] with `personId == 7`; the body does not
contain `Tomas` or `Pilot`.

**S3 (Theory) Read path, payload-shaped unknown name.** `b' OR Id IS 3 OR 'b`, `b' OR 1 OR 'b`: When GET with it,
Then 404, `person` null, `astronautDuties` [], body does not contain `Tomas`, `Mira` or the payload.

**S4 (Theory) Rule-failing payloads are 400 before SQL, on all three endpoints.** Rows: `x' OR '1'='1`,
`'; DROP TABLE Person;--`, `a"; SELECT 1`, `Robert'); DROP TABLE AstronautDuty;--`.
- `POST /Person {name}`, `GET /AstronautDuty?name=<escaped payload>` and
  `POST /AstronautDuty {name: payload, rank:"CPT", dutyTitle:"Pilot", dutyStartDate:"2026-03-01"}` each -> 400,
  errors name key == [NameRule], no echo.
- Then: nothing written; `Person`, `AstronautDetail`, `AstronautDuty` tables all still exist.

**S5 Apostrophe name end to end.** Given `POST /Person {"name":"Aoife O'Brien Jr."}` (201), When
`POST /AstronautDuty` for that name (Pilot/CPT/2026-03-01), Then 201, numeric `id`, and `Location` is exactly
`/AstronautDuty?name=Aoife%20O%27Brien%20Jr.`; When GET of the Location and of the literal URL, Then 200 with that
person and exactly the new duty (same id). No 500 anywhere.

**S6 rank and dutyTitle are data.** Given Kenji, When POST with `rank` `O'Neil` and `dutyTitle` `Pilot' OR '1`
(both valid), Then 201 and the duty and detail store exactly `O'Neil` / `Pilot' OR '1`; others unchanged.

**S6b (Theory)** `rank` or `dutyTitle` = `x' OR '1'='1` or `a;b` -> 400 under that key with [NameRule], no echo,
nothing written.

**S7 Padded and differently-cased hostile name resolves to the same person.** Given S1's person
`a' OR Id IS 3 OR 'a` with one duty, When POST a second duty (2026-04-01) with name `  A' or id is 3 or 'A  `, Then 201,
the first duty is closed on 2026-03-31, the new one is open, others unchanged, and Location uses the STORED name
(`/AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a`).

## 2. GET /AstronautDuty (`AstronautDutyHttpTests`, prefix G)

- **G1** Given seeded Elena, When `GET /AstronautDuty?name=Elena%20Vasquez`, Then 200; envelope keys exactly
  {astronautDuties, message, person, responseCode, success}, success true, message `Successful`, responseCode 200;
  `person` = Elena (Id 5, MAJ, Commander, careerStartDate `2012-01-09T00:00:00`, careerEndDate null);
  `astronautDuties` titles in order [Commander, Pilot, Flight Engineer]; each item has exactly the keys {id, personId,
  rank, dutyTitle, dutyStartDate, dutyEndDate}; start dates match `^\d{4}-\d{2}-\d{2}T00:00:00$`; the first item's
  end is null and the second's is `2018-12-31T00:00:00`.
- **G2 (Theory)** `elena%20vasquez`, `%20ELENA%20VASQUEZ%20`, `ELENA%20VASQUEZ%09` all return Elena (200).
- **G3 (Theory)** `?name=ra` and `?name=Elena` -> 404 (fragments never match).
- **G4** Mira (no assignment) -> 200, `currentRank` and `currentDutyTitle` "", `careerStartDate` null,
  `astronautDuties` [] (rule 2).
- **G5** `?name=Nobody` -> 404, body keys exactly {astronautDuties, message, person, responseCode, success} with
  {person:null, astronautDuties:[], success:false, message:NotFound, responseCode:404}; body does not contain `Nobody`.
- **G6 (Theory)** `/AstronautDuty`, `/AstronautDuty?name=` and `/AstronautDuty?name` -> 400, errors.name == [RequiredName].
- **G7** `?name=%20%20` -> 400, errors.name == [RequiredName] only (Amendment 1: the query binder turns a
  whitespace-only value into null, so `ValidName` never sees it).
- **G7b (Theory)** `?name=%09` and `?name=%0A` -> 400, errors.name == [RequiredName] (same as other whitespace).
- **G7c (Theory) Repeated parameter.** `?name=Elena%20Vasquez&name=Tomas%20Lindqvist` -> 200 Elena;
  `?name=Tomas%20Lindqvist&name=Elena%20Vasquez` -> 200 Tomas. The FIRST value binds; every item's `personId` equals
  `person.personId` (values never merged).
- **G8 (Theory)** Decoded values (sent through `Uri.EscapeDataString`): 101 x `a`, `a=b`, `%`, `José`, `..`, `-`,
  and the four S4 payloads -> 400, errors.name == [NameRule], no echo (raw-body check only for values longer than 2
  characters, because `-` and `..` occur in the traceId and type URL), nothing written, the three tables still exist.
- **G8b** 100 x `a` -> 404 (valid, unknown).
- **G9** Desmond -> [RETIRED, Commander, Pilot]; `careerEndDate` `2023-03-31T00:00:00`.
- **G10** Tie-break: Given two duties for Kenji inserted directly with the same start date (ids x < y), When GET,
  Then y is listed before x.
- **G11 (Theory)** Elena (5) and Desmond (6): `person.personId` as expected, three items, every item's `personId`
  equals it.
- **G12 (Theory)** `GET /AstronautDuty/Elena%20Vasquez` and `/AstronautDuty/Nobody` -> 404 with an EMPTY body (routing).
- **G13** Unknown extra query parameters are ignored (`?name=Elena%20Vasquez&rank=X` -> 200 Elena).

## 3. POST /AstronautDuty (`AstronautDutyHttpTests`, prefix A)

Valid base body: `{"name":"Kenji Watanabe","rank":"CPT","dutyTitle":"Pilot","dutyStartDate":"2024-01-01"}`.

- **A1** Base body -> 201; body keys exactly {id, success, message, responseCode}; id integer >= 1; success true;
  message `Successful`; responseCode 201; `Location` `/AstronautDuty?name=Kenji%20Watanabe`; following it returns 200
  with the new duty (id equal) and Kenji's detail CPT/Pilot/careerStartDate 2024-01-01.
- **A2** Name `  kenji WATANABE ` -> 201 for Kenji; Location uses the stored `Kenji%20Watanabe`.
- **A3** Unknown name `Nobody` -> 404, keys exactly {id, message, responseCode, success} with
  {id:null, success:false, message:NotFound, responseCode:404}, no Location, nothing written, body does not contain `Nobody`.
- **A3b (Theory)** A fragment of an existing name (`Kenji`, `Tomas`) -> 404 NotFound, nothing written.
- **A3c (Theory)** A payload-shaped unknown name (`b' OR 1 OR 'b`, `b' OR Id IS 3 OR 'b`) -> 404 NotFound, no echo
  (the raw body does not contain `OR`), no Location, nothing written.
- **A4** Duplicate: base body twice -> 201 then 409, keys exactly {id, message, responseCode, success} with
  {id:null, success:false, message:Dup, responseCode:409}, no Location, the second request writes nothing.
- **A5** Duplicate ignoring time and case: after the base body, `dutyTitle:"PILOT"`,
  `dutyStartDate:"2024-01-01T15:30:00"` -> 409 Dup, nothing written.
- **A6** Not a duplicate for a different person: after the base body, the same title/date for Mira -> 201 with
  Location `/AstronautDuty?name=Mira%20Okafor`.
- **A7** Same person, same date, different title -> 201 (not this task's rule; pins today's acceptance for task 009).
- **A8 (Theory)** For each of `name`, `rank`, `dutyTitle`: missing -> [Required]; `null` -> [Required]; `""` and
  `"   "` -> [Required, NameRule]; `x' OR '1'='1`, `a;b`, 101 x `a`, `José`, `..` -> [NameRule]. Required =
  `The <Name|Rank|DutyTitle> field is required.`. The error is under that key (case-insensitive) and is the only key;
  no echo of a non-blank value; no Location; nothing written.
- **A9 (Theory)** `dutyStartDate` missing or `null` -> 400 ["The DutyStartDate field is required."]; `"0001-01-01"`,
  `"1899-12-31"`, `"2101-01-01"` -> 400 [DateMsg]. Nothing written.
- **A9b (Theory)** Bounds are inclusive (Mira): `"1900-01-01"`, `"2100-12-31"` -> 201 stored at midnight;
  `"2100-12-31T23:59:59"` -> 201 stored as `2100-12-31 00:00:00`.
- **A9c (Theory)** `"not-a-date"`, `"2024-13-01"`, `12345`, `{}` -> 400 with at least one error, generic messages only
  (`ProblemAssert.NoParserInternals`), no echo, nothing written.
- **A10 (Theory)** Malformed bodies (`[]`, `"Kenji"`, `{"name":4242424242,...}`, two truncated forms, empty) -> 400,
  at least one error, `NoParserInternals`, `4242424242` not echoed, nothing written.
- **A11 (Theory)** Content types `text/plain`, `application/xml`, `application/x-www-form-urlencoded` -> 415 with an
  empty body, nothing written.
- **A11b** No content and no Content-Type -> 415, nothing written.
- **A11c** `application/json; charset=utf-8` -> 201.
- **A12** Unknown properties (`id: 999`, `personId: 3`) are ignored: 201, the new duty belongs to Kenji, its id is
  not 999, no row has Id 999, others (including Tomas) unchanged.
- **A13** Order of checks: invalid rank for an unknown person -> 400 (not 404); duplicate with an invalid title ->
  400 (not 409); unknown person with a would-be duplicate title/date -> 404.
- **A14** `responseCode` equals the HTTP status on 201, 409, 404.
- **A15** Rank and title are stored trimmed: Elena with `rank:" MAJ "`, `dutyTitle:"\tRETIRED "`, `2025-01-01` ->
  201; the new duty is `MAJ|RETIRED|2025-01-01|<open>`, the detail's CurrentDutyTitle is `RETIRED` and CareerEndDate
  is 2024-12-31 (a padded RETIRED counts as retired).
- **A17** A successful create for Tomas (MAJ, Commander, 2024-03-01) closes his Pilot duty on 2024-02-29, adds the
  open Commander duty, and changes no other person's rows (`DumpExcept(3)`).
- **A20** Swagger (`/swagger/v1/swagger.json`): no path `/AstronautDuty/{name}`; `/AstronautDuty` `get` has exactly one
  parameter, `name` in query, `required: true`, and responses exactly {200, 400, 404, 500}; `post` has summary
  `Add a duty to a person`, responses exactly {201, 400, 404, 409, 415, 500} and request content only
  `application/json`; schema `CreateAstronautDutyRequest` has properties and `required` both exactly
  {dutyStartDate, dutyTitle, name, rank}; `CreateAstronautDutyResult` has no `name` property; `AstronautDutyItem` has
  exactly the six properties.

## 4. Handlers (`GetAstronautDutiesByNameHandlerTests` H*, `CreateAstronautDutyHandlerTests` CH*)

`NullPersonGuardTests.cs` is deleted. Mapping of its tests:
`GetDuties_UnknownName_ReturnsDefaultResultWithoutThrowing` -> rewritten as H2 (404);
`GetDuties_KnownName_ReturnsPersonAndDutiesNewestFirst` -> rewritten as H1;
`GetDuties_PersonWithoutAstronautDetail_CharacterisesCurrentOutput` -> rewritten as H3;
`CreateDuty_UnknownPerson_Returns404AndWritesNothing` -> rewritten as CH2 (new constructor);
`CreateDuty_KnownPerson_StillCreatesDetailAndDuty` -> rewritten as CH1 (201);
`PreProcessor_UnknownPerson_ThrowsBadRequestAndWritesNothing`, `PreProcessor_KnownPerson_DoesNotThrowAndWritesNothing` -> deleted (type removed).

- **H1** Elena -> 200, Success, `Successful`, person Id 5 MAJ/Commander, duties [Commander, Pilot, Flight Engineer],
  all `PersonId` 5, first end null, second end 2018-12-31, third start 2012-01-09.
- **H2** Unknown name -> ResponseCode 404, Success false, Message NotFound, Person null, AstronautDuties empty.
- **H3** Mira -> 200, `""` strings, null dates, empty duties.
- **H4 (Theory)** `" elena VASQUEZ\t"`, `"ELENA VASQUEZ"` -> Elena with three duties.
- **H5 (Theory)** Injection, read: a person stored as `a' OR Id IS 3 OR 'a` (or `a' OR 1 OR 'a`) with no duties;
  query with that name -> that person (Id 7), empty duties; the same payload with `b` (nobody's name) -> 404.
- **H6** Already-cancelled token -> `Assert.ThrowsAnyAsync<OperationCanceledException>`.
- **H7** Tie-break: two duties with the same start date are listed higher id first.
- **H8** Pre-index schema: on a database migrated only to `MakeCareerStartDateRequired` (no NOCASE on
  `Person.Name`, asserted), a stored `Elena Vasquez` is found by `elena VASQUEZ` (the query folds case itself).
- **CH1** Kenji first duty -> 201, Success, `Successful`, Id >= 1 and equal to the stored row, `Name` == `Kenji Watanabe`,
  duty and detail CPT/Pilot/2024-01-01, open, CareerEndDate null.
- **CH2** Unknown person -> 404, Success false, NotFound, Id null, Name null, nothing written.
- **CH3 Regression, rules 3-7 "second duty arrives" (normal case).** Given seeded Tomas (Pilot/CPT 2021-06-14 open;
  detail Pilot/CPT, CareerStart 2021-06-14), When `{Tomas Lindqvist, MAJ, Commander, 2024-03-01T15:30:00}`, Then:
  Pilot duty `DutyEndDate` = 2024-02-29; new duty MAJ/Commander start 2024-03-01 00:00, end null; detail
  MAJ/Commander, CareerStartDate 2021-06-14 (unchanged), CareerEndDate null; exactly one open duty for Tomas; others unchanged.
- **CH4 Regression, RETIRED as a later duty.** Elena + `{MAJ, RETIRED, 2025-01-01}` -> Commander ends 2024-12-31,
  detail MAJ/RETIRED, CareerEndDate 2024-12-31, Flight Engineer and Pilot rows unchanged, others unchanged.
- **CH5 Regression, first duty.** Mira + `{CPT, Pilot, 2024-01-01}` -> detail created, CareerStartDate 2024-01-01,
  CareerEndDate null, one open duty. (The first-RETIRED defect is NOT pinned.)
- **CH6** Duplicate scoped: same title/date for another person -> 201; same person -> 409 Dup, Id and Name null,
  nothing written.
- **CH7** Duplicate ignores title case (`pilot` vs `Pilot`) -> 409 Dup, nothing written.
- **CH8** Tie-break: Given two open duties for Kenji at the same latest start date, When a later duty arrives, Then
  the one with the higher Id gets the end date, the other stays open.
- **CH9** Time part on stored data: Given a duty inserted directly with `DutyStartDate` `2024-01-01 10:00:00`, When a
  request for the same person/title/`2024-01-01` arrives, Then 409 Dup, nothing written (plan U2).
- **CH10 Race on the existing unique index.** Given Mira (no detail) and `SqlOnAnotherConnectionBeforeSave` inserting
  an `AstronautDetail` and an open duty for Mira, When the handler runs, Then 409 Race, Id null, and the only rows for
  Mira are the ones the interceptor inserted.
- **CH11 Parameters only (create path).** With `CountingCommandInterceptor`, a stored person `zq' OR Id IS 3 OR 'zq`
  and a create with rank `CPT` and title `Pilotzq' OR '1` -> 201, at least one command recorded, no recorded
  `CommandText` contains `zq` (case-insensitive), and the title is stored verbatim.
- **CH12** Already-cancelled token -> `OperationCanceledException`, nothing written.
- **CH12b** Token cancelled during `SaveChangesAsync` (`CancelDuringSave` interceptor) -> `OperationCanceledException`,
  nothing written (the save is atomic).
- **CH13 (Theory)** Guard: name `x' OR '1'='1`, rank `a;b`, title `""`, year 1, year 2101 passed directly ->
  `ArgumentException` with message exactly `Invalid duty request. (Parameter 'request')`, nothing written.
- **CH14** Logging (`CapturingLogger`): a create logs exactly one Information entry
  `Created duty <id> for person 2`; the rejections log exactly `Create duty rejected: person not found` and
  `Create duty rejected: duplicate for person 2`; no entry contains the name or the title.
- **CH15 (Theory)** Name `"  kenji WATANABE\t"` or `"KENJI WATANABE"`, rank `" MAJ "`, title `"\tCommander "` ->
  201 for Kenji (`Name` == `Kenji Watanabe`); duty and detail store `MAJ`/`Commander`.
- **CH16** Tomas with title `" RETIRED "` (2025-01-01) -> detail CurrentDutyTitle `RETIRED`, CareerEndDate 2024-12-31.
- **CH17** Pre-index schema (as H8): a stored `Kenji Watanabe` is found by `" kenji WATANABE "` -> 201, `Name` ==
  `Kenji Watanabe`.

## 5. Other API tests

- **K1 (Kestrel)** `POST /Person` + `POST /AstronautDuty` for `Aoife O'Brien Jr.` -> both 201, `Location`
  OriginalString exactly `/AstronautDuty?name=Aoife%20O%27Brien%20Jr.`, and a GET of it is 200 containing the title.
- **K2 (Kestrel, Production, never-migrated DB)** `GET ?name=Secretname%20Probe` -> 500 with an EMPTY body (so no
  `no such table`, `SqliteException`, `Secretname` or `Exception`).
- **K2b (Kestrel, Production, never-migrated DB)** POST with name `Secretname Probe` -> 500 with an EMPTY body.
- **K3 (Kestrel)** POST body of 4097 bytes (valid JSON, space-padded) -> 413, nothing written; exactly 4096 bytes -> 201.
- **U4 (Kestrel)** `GET /AstronautDuty/Elena%20Vasquez` on real Kestrel -> 404 with an empty body (plan U4).
- **A16 (Kestrel, Development, theory GET/POST)** On a migrated, seeded DB whose `AstronautDuty` table has been
  dropped, GET `?name=Elena%20Vasquez` and the base POST -> 500, Content-Type is not `application/json`, and the body
  contains neither a `"success"` nor a `"responseCode"` member.
- **C1 (`AstronautDutyControllerTests`)** The GET action sends `GetAstronautDutiesByName` with the name and the
  request's `CancellationToken` (fake `IMediator`), and returns a 200 `ObjectResult` of `GetAstronautDutiesByNameResult`.
- **C2** The POST action passes the token and the request values unchanged to `CreateAstronautDuty`, and returns a
  `CreatedResult` (201) with Location `/AstronautDuty?name=Kenji%20Watanabe`.
- **R1 (`ResponseShapeTests.GetAstronautDutiesByNameResult_HasTheDocumentedShape_AndItemsHaveExactlySixMembers`)**
  The result serialises to {person, astronautDuties, success, message, responseCode}; items have exactly the six
  keys (no `person`), with `dutyEndDate` null or a string.
- **R2 (`ResponseShapeTests.CreateAstronautDutyResult_HasTheDocumentedShape_AndNeverSerialisesTheName`)**
  `{ Id = 9, Name = "x" }` serialises to exactly {id, success, message, responseCode} (no `name`); a default result
  writes `id: null`.
- **V1 (`InputRulesTests`, theory)** `IsValidDutyDate` and `ValidDutyDateAttribute.IsValid`: 1899-12-31 false,
  1900-01-01 true, 2100-12-31 true, 2100-12-31T23:59:59 true, 2101-01-01 false, 0001-01-01 false.
- **V1b** `ValidDutyDateAttribute`: null valid, a string invalid, `DateTime.MinValue` invalid, message exactly DateMsg.

## 5a. Person resolution on a pre-index database (`PersonResolutionPreIndexHttpTests`)

Fixture: `DutyApiHost(seed: false, migrateTo: MakeCareerStartDateRequired, environment: "Production")`; people
`bob` (Id 4) and `Bob` (Id 9), each with one duty (`Lowercase Bob Duty`, `Uppercase Bob Duty`).

- **P1** `PostWithTheExactCaseName_WritesToThatPerson_NotTheLowestId`: POST for `Bob` -> 201, Location
  `/AstronautDuty?name=Bob`, the new duty is on Id 9 only.
- **P2** `PostWithTheLowerCaseName_WritesToThePersonWithThatExactName`: POST for `bob` -> 201 on Id 4 only.
- **P3** `PostWithANameThatMatchesNeitherExactly_WritesToTheLowestId`: POST for `BOB` -> 201 on Id 4 only.
- **P4 (Theory)** `GetDuties_AndGetPerson_ResolveTheSamePerson`: `Bob` -> 9, `bob` -> 4, `BOB` -> 4, for both
  `GET /AstronautDuty?name=` (with that person's single duty) and `GET /Person/{name}`.

(P1-P4 are labels for this file only; the test methods carry no ID prefix.)

## 6. Mutation map (each mutation must fail at least the listed tests)

| Mutation | Fails |
|---|---|
| Interpolated person lookup in create | S1, S7, CH11 |
| Interpolated SQL in GET | S2, S3, H5 |
| Explicit `COLLATE NOCASE` / `EF.Functions.Collate(..., "NOCASE")` dropped in the GET / create lookup | H8 / CH17, and P3, P4 (`BOB`) for the respective path. **Equivalent on a migrated database** (the column is NOCASE), so G2, A2, H4 and S7 still pass. |
| Exact-case preference dropped (order by Id only) in GET / create | P4 (`Bob`) / P1 |
| `COLLATE BINARY` dropped from the GET CASE, or `Collate(..., "BINARY")` dropped in create | None: **equivalent mutation** (BINARY is the column default before the index migration; after it at most one row matches) |
| Name `Normalize` dropped (GET / create) | G2, H4 / A2, S7, CH15, CH17 |
| Rank/title `Normalize` dropped | A15, CH15, CH16 |
| Duplicate check unscoped / full datetime / case-sensitive | A6+CH6 / A5+CH9 / A5+CH7 |
| 404 <-> 400 <-> 409 swapped; order of checks changed | A3, A3b, A3c, A4, A13, G5 |
| 201 -> 200, or `Created` without Location | A1, K1, C2 |
| Location unencoded or built from the request name | K1, S5, A2, S7 |
| CancellationToken not passed | C1, C2, H6, CH12, CH12b |
| try/catch with `ex.Message` reintroduced | K2, K2b, A16 |
| Tie-break dropped (GET order / create close) | G10, H7 / CH8 |
| Date bounds removed | A9, V1, V1b, CH13 |
| `GetPersonByName` sent again from GET | G1 (no duties) |
| A pre-handler lookup that throws `BadHttpRequestException` reintroduced | A3 (400 instead of 404) |
| A repeated `name` bound to the last value or joined | G7c |

## 7. UI (`stargate-api.spec.ts`, `describe('getDuties')`)

- **D1** Given `getDuties('Elena Vasquez')`, Then exactly one request, method GET, `url` `/api/AstronautDuty`,
  `params.keys()` == ['name'], `urlWithParams` `/api/AstronautDuty?name=Elena%20Vasquez`.
- **D2** `getDuties("Aoife O'Brien Jr.")` -> `urlWithParams` `/api/AstronautDuty?name=Aoife%20O'Brien%20Jr.`;
  `getDuties('a&b+c#d')` -> `/api/AstronautDuty?name=a%26b%2Bc%23d`. No path segment after `AstronautDuty`.
- **D3** 200 with person and two duties -> emits `{status:'found', person, astronautDuties}` with the duties in server
  order; completes; no error.
- **D4** 200 with `astronautDuties: []` -> `found` with `[]` (empty state).
- **D5** 404 with the not-found body -> emits `{status:'notFound'}`, completes, no error.
- **D6** 400 with a `ValidationProblemDetails` -> `{status:'invalid', errors}` equal to the body's errors; 400 without
  `errors` -> `errors` `{}`.
- **D7** 500, 401, 403 and status 0 (network error) -> error is an `HttpErrorResponse` with that status; nothing
  emitted (never `notFound`).
- **D8** 200 with `person: null`, and 200 with `astronautDuties` missing -> error `Malformed duties response`.
- **D9** Sent unmodified: `getDuties('  Elena Vasquez ')` -> `?name=%20%20Elena%20Vasquez%20`; `getDuties('')` ->
  a request with `?name=` is still made.
- `npm test -- --watch=false` and `npm run build` pass; no `any`.
