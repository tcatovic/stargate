# Task 007: tests each platform must satisfy (revision 1)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted
state), not implementation. IDs are unique within this file. Literals (messages) are written out in tests, not taken
from the production constants (existing `ProblemAssert` style).

Fixture conventions (API):
- HTTP tests: `WebApplicationFactory<Program>`, Development environment, a per-test `TestDatabase` migrated and
  seeded with `DevelopmentSeedData.People()` (ids: Mira 1, Kenji 2, Tomas Lindqvist 3, Priya 4, Elena 5, Desmond 6;
  each hostile test first asserts `Person.Id 3` is `Tomas Lindqvist`). `AllowAutoRedirect = false`.
- New test helper `TestDatabase.DumpExcept(long personId)`: like `Dump()` but excludes `Person` rows with
  `Id = personId` and `AstronautDetail`/`AstronautDuty` rows with `PersonId = personId` (use a SQLite parameter).
  "Others unchanged" always means `DumpExcept(target)` before == after.
- "Nothing written" means `Dump()` before == after.
- "No echo" means `ProblemAssert.NothingDecodedContains(json, submitted...)` and, for envelopes, the raw body does not
  contain the submitted value.
- Messages: NotFound = `Person not found`; Dup = `A duty with this title and start date already exists for this person`;
  Race = `The person's duties were changed by another request`; DateMsg = `Use a date between 1900-01-01 and 2100-12-31.`;
  NameRule = the existing `ProblemAssert.NameRuleMessage`.

## 1. Security: hostile matrix over HTTP (`AstronautDutySecurityHttpTests`)

These must fail on `main` 280fcd9 (write tests: another person's rows change or a 500; read tests: route missing).

**S1 (Theory) Write path, a valid-rule payload is just a name.** Rows:
`a' OR Id IS 3 OR 'a`, `a' OR 1 OR 'a`, `a' OR 'a' IS 'a`, `Tomas Lindqvist'--`, `Aoife O'Brien Jr.`, `O'Neil-Smith Jr.`.
- Given the seeded DB, When `POST /Person {name}`, Then 201 and the new id `T`.
- Given `before = DumpExcept(T)`, When `POST /AstronautDuty {name, rank:"CPT", dutyTitle:"Injected Role", dutyStartDate:"2026-03-01"}`,
  Then 201, `Location` = `/AstronautDuty?name=` + `Uri.EscapeDataString(name)`, exactly one `AstronautDuty` row with
  `PersonId = T` (CPT, Injected Role, 2026-03-01, end null), one `AstronautDetail` with `PersonId = T`, and
  `DumpExcept(T)` == `before` (in particular Tomas's Pilot duty still has `DutyEndDate` null and his detail is Pilot/CPT).

**S2 Read path, own duties only.** Given S1's state for `a' OR Id IS 3 OR 'a`, When
`GET /AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a`, Then 200, `person.personId == T`,
`person.name` is the payload, `astronautDuties` has exactly one item with `personId == T`; the body does not contain
`Tomas` or `Pilot`.

**S3 Read path, payload-shaped unknown name.** Given no person named `b' OR Id IS 3 OR 'b` (or `b' OR 1 OR 'b`),
When GET with it, Then 404 NotFound envelope, `person` null, `astronautDuties` [], body does not contain `Tomas`,
`Mira` or the payload.

**S4 (Theory) Rule-failing payloads are 400 before SQL, on all three endpoints.** Rows: `x' OR '1'='1`,
`'; DROP TABLE Person;--`, `a"; SELECT 1`, `Robert'); DROP TABLE AstronautDuty;--`.
- `POST /Person {name}` -> 400, errors.name == [NameRule], no echo.
- `POST /AstronautDuty {name: payload, rank:"CPT", dutyTitle:"Pilot", dutyStartDate:"2026-03-01"}` -> 400, errors
  name key == [NameRule], no echo.
- `GET /AstronautDuty?name=<escaped payload>` -> 400, errors.name == [NameRule], no echo.
- Then: nothing written; `Person`, `AstronautDetail`, `AstronautDuty` tables all still exist with the seeded counts.

**S5 Apostrophe name end to end.** Given `POST /Person {"name":"Aoife O'Brien Jr."}` (201), When
`POST /AstronautDuty` for that name (Pilot/CPT/2026-03-01), Then 201 and `Location` is exactly
`/AstronautDuty?name=Aoife%20O%27Brien%20Jr.`; When `GET /AstronautDuty?name=Aoife%20O%27Brien%20Jr.` (and the
Location as given), Then 200 with that person and the duty. No 500 anywhere.

**S6 (Theory) rank and dutyTitle are data.** Given Kenji, When POST with `rank` `O'Neil` and `dutyTitle`
`Pilot' OR '1` (both valid) Then 201 and the stored values are exactly `O'Neil` / `Pilot' OR '1`, others unchanged.
When `rank` or `dutyTitle` is `x' OR '1'='1` or `a;b` Then 400 under that key with [NameRule], no echo, nothing written.

**S7 Padded and differently-cased hostile name resolves to the same person.** Given S1's person
`a' OR Id IS 3 OR 'a`, When POST a second duty (later date) with name `  A' or id is 3 or 'A  `, Then 201 for `T`,
others unchanged, Location uses the STORED name.

## 2. GET /AstronautDuty (`AstronautDutyHttpTests`, prefix G)

- **G1** Given seeded Elena, When `GET /AstronautDuty?name=Elena%20Vasquez`, Then 200; envelope success true,
  message `Successful`, responseCode 200; `person` = Elena (MAJ, Commander, careerStartDate `2012-01-09T00:00:00`,
  careerEndDate null); `astronautDuties` titles in order [Commander, Pilot, Flight Engineer]; each item has exactly
  the keys {id, personId, rank, dutyTitle, dutyStartDate, dutyEndDate} (no `person`); dates match `^\d{4}-\d{2}-\d{2}T00:00:00$`.
- **G2 (Theory)** `elena%20vasquez`, `%20ELENA%20VASQUEZ%20`, `ELENA%20VASQUEZ%09` all return Elena (200).
- **G3** `?name=ra` and `?name=Elena` -> 404 (fragments never match).
- **G4** Mira (no assignment) -> 200, `person.currentRank` "", `careerStartDate` null, `astronautDuties` [] (rule 2).
- **G5** `?name=Nobody` -> 404, body exactly {person:null, astronautDuties:[], success:false, message:NotFound,
  responseCode:404}; body does not contain `Nobody`.
- **G6** missing `name` and `?name=` -> 400, errors.name == ["The name field is required."].
- **G7** `?name=%20%20` -> 400, errors.name == ["The name field is required.", NameRule].
- **G8 (Theory)** 101 x `a`, `a=b`, `%25`, `Jos%C3%A9`, `..`, `-` -> 400, errors.name == [NameRule], no echo.
  100 x `a` -> 404 (valid, unknown).
- **G9** Desmond -> [RETIRED, Commander, Pilot]; `careerEndDate` `2023-03-31T00:00:00`.
- **G10** Tie-break: Given a person with two duties inserted directly with the same start date (ids x < y), When GET,
  Then y is listed before x.
- **G11** Duties of other people never appear: every item's `personId` == `person.personId` (Elena, Desmond).
- **G12** `GET /AstronautDuty/Elena%20Vasquez` -> 404 with an EMPTY body (routing), and the same for
  `/AstronautDuty/Nobody`. (Also asserts nothing echoes.)
- **G13** Unknown extra query parameters are ignored (`?name=Elena%20Vasquez&rank=X` -> 200 Elena).

## 3. POST /AstronautDuty (`AstronautDutyHttpTests`, prefix A)

Valid base body: `{"name":"Kenji Watanabe","rank":"CPT","dutyTitle":"Pilot","dutyStartDate":"2024-01-01"}`.

- **A1** Base body -> 201; body keys exactly {id, success, message, responseCode}; id integer >= 1; success true;
  message `Successful`; responseCode 201; `Location` `/AstronautDuty?name=Kenji%20Watanabe`; following it returns 200
  with the new duty (id equal) and Kenji's detail CPT/Pilot/careerStartDate 2024-01-01.
- **A2** Name `  kenji WATANABE ` -> 201 for Kenji; Location uses the stored `Kenji%20Watanabe`.
- **A3** Unknown name `Nobody` -> 404 {id:null, success:false, message:NotFound, responseCode:404}, no Location,
  nothing written, body does not contain `Nobody`.
- **A4** Duplicate: base body twice -> 201 then 409 {id:null, success:false, message:Dup, responseCode:409}, no
  Location, second request writes nothing.
- **A5** Duplicate ignoring time and case: after A1, `dutyTitle:"PILOT"`, `dutyStartDate:"2024-01-01T15:30:00"` -> 409.
- **A6** Not a duplicate for a different person: after A1, the same title/date for Mira -> 201.
- **A7** Same person, same date, different title -> 201 (not this task's rule; pins today's acceptance for task 009).
- **A8 (Theory)** Missing / null / empty / whitespace / invalid for each of `name`, `rank`, `dutyTitle` -> 400 with
  the exact message list from the contract under that key (case-insensitive key), no echo, nothing written.
- **A9 (Theory)** `dutyStartDate` missing or null -> 400 ["The DutyStartDate field is required."]; `"0001-01-01"`,
  `"1899-12-31"`, `"2101-01-01"` -> 400 [DateMsg]; `"1900-01-01"` and `"2100-12-31"` (for a fresh person) -> 201;
  `"not-a-date"` and `"2024-13-01"` -> 400 with generic messages only (`ProblemAssert.NoParserInternals`). Nothing
  written on any 400.
- **A10** Malformed bodies (`[]`, `"Kenji"`, `{"name":4242,...}`, truncated, empty) -> 400, `NoParserInternals`,
  nothing written.
- **A11 (Theory)** Content types `text/plain`, `application/xml`, `application/x-www-form-urlencoded` -> 415 empty
  body; no content and no Content-Type -> 415; `application/json; charset=utf-8` -> 201.
- **A12** Unknown properties (`id: 999`, `personId: 3`) are ignored: 201, new duty belongs to Kenji, its id is not 999,
  Tomas unchanged.
- **A13** Order of checks: invalid rank for an unknown person -> 400 (not 404); duplicate with an invalid title ->
  400 (not 409); unknown person with a would-be duplicate title/date -> 404.
- **A14** `responseCode` equals the HTTP status on 201, 404, 409.
- **A15** Rank and title are stored trimmed: `rank:" MAJ "`, `dutyTitle:"\tCommander "` -> stored `MAJ`/`Commander`
  in the duty and the detail.
- **A16** Development 500 is not a JSON envelope: with the controller unable to reach the DB (unmigrated temp DB),
  POST and GET return 500 whose body does not contain `success` / `message` JSON members.
- **A20** Swagger (`/swagger/v1/swagger.json`): path `/AstronautDuty` has `get` with a `name` query parameter
  `required: true` and responses 200/400/404/500; `post` with responses 201/400/404/409/415 (500 may be listed); no path
  `/AstronautDuty/{name}`.

## 4. Handlers (`GetAstronautDutiesByNameHandlerTests` H*, `CreateAstronautDutyHandlerTests` CH*)

Replaces `NullPersonGuardTests.cs` (deleted). Mapping:
`GetDuties_UnknownName_ReturnsDefaultResultWithoutThrowing` -> rewritten as H2 (404);
`GetDuties_KnownName_ReturnsPersonAndDutiesNewestFirst` -> kept as H1;
`GetDuties_PersonWithoutAstronautDetail_CharacterisesCurrentOutput` -> kept as H3;
`CreateDuty_UnknownPerson_Returns404AndWritesNothing` -> kept as CH2 (new constructor);
`CreateDuty_KnownPerson_StillCreatesDetailAndDuty` -> rewritten as CH1 (201);
`PreProcessor_UnknownPerson_ThrowsBadRequestAndWritesNothing`, `PreProcessor_KnownPerson_DoesNotThrowAndWritesNothing` -> deleted (type removed).

- **H1** Known name -> 200, person, duties newest first, only that person's duties.
- **H2** Unknown name -> ResponseCode 404, Success false, Message NotFound, Person null, AstronautDuties empty.
- **H3** Person without detail -> 200, `""` strings, null dates, empty duties.
- **H4** Case-insensitive and trimmed (`" elena VASQUEZ\t"`) -> Elena.
- **H5** Injection, read: a person stored as `a' OR Id IS 3 OR 'a` with no duties; query with that name -> that
  person, empty duties (never Tomas). Fails on `main`'s handler.
- **H6** Already-cancelled token -> `Assert.ThrowsAnyAsync<OperationCanceledException>` (the token reaches Dapper via `CommandDefinition`).
- **CH1** Kenji first duty -> 201, Id >= 1, `Name` == `Kenji Watanabe`, detail created (CareerStartDate = start, CareerEndDate null).
- **CH2** Unknown person -> 404, Id null, nothing written.
- **CH3 Regression, rules 3-7 "second duty arrives" (normal case).** Given seeded Tomas (Pilot/CPT 2021-06-14 open;
  detail Pilot/CPT, CareerStart 2021-06-14), When `{Tomas Lindqvist, MAJ, Commander, 2024-03-01T15:30:00}`, Then:
  Pilot duty `DutyEndDate` = 2024-02-29; new duty MAJ/Commander start 2024-03-01 00:00, end null; detail
  CurrentRank MAJ, CurrentDutyTitle Commander, CareerStartDate 2021-06-14 (unchanged), CareerEndDate null; every other
  person's rows unchanged; exactly one open duty for Tomas.
- **CH4 Regression, RETIRED as a later duty.** Elena + `{RETIRED, MAJ, 2025-01-01}` -> Commander ends 2024-12-31,
  detail CurrentDutyTitle RETIRED, CareerEndDate 2024-12-31, Flight Engineer and Pilot rows unchanged.
- **CH5 Regression, first duty.** Mira + `{CPT, Pilot, 2024-01-01}` -> detail created, CareerStartDate 2024-01-01,
  CareerEndDate null, one open duty. (The first-RETIRED defect is NOT pinned.)
- **CH6** Duplicate scoped: same title/date for another person -> 201; same person -> 409 Dup, nothing written.
- **CH7** Duplicate ignores title case (`pilot` vs `Pilot`) -> 409.
- **CH8** Tie-break: Given a person with two open-ended duties seeded at the same latest start date, When a later
  duty arrives, Then the one with the higher Id gets the end date, the other is unchanged.
- **CH9** Time part on stored data: Given a duty inserted directly with `DutyStartDate` `2024-01-01 10:00:00`, When a
  request for the same person/title/`2024-01-01` arrives, Then 409 (verifies the EF `.Date` translation, plan U2).
- **CH10 Race on the existing unique index.** Given Mira (no detail) and a context built with
  `SqlOnAnotherConnectionBeforeSave` inserting an `AstronautDetail` (and an open duty) for Mira, When the handler
  runs, Then 409 Race, and the only rows for Mira are the ones the interceptor inserted.
- **CH11 Parameters only (create path).** With `CountingCommandInterceptor` attached, store a person named
  `zq' OR Id IS 3 OR 'zq` and run a create for that name with rank `CPT` and title `Pilotzq' OR '1`; Then 201, at least
  one command was recorded, and no recorded `CommandText` contains `zq` (the values travelled only as parameters).
  Guards the EF path; Dapper is not used on this path.
- **CH12** Already-cancelled token -> `Assert.ThrowsAnyAsync<OperationCanceledException>`, nothing written.
- **CH13** Guard: invalid name/rank/title/date passed directly -> `ArgumentException` whose message does not contain the value.
- **CH14** Logging (CapturingLogger): a create logs one Information entry containing the duty id and person id and not
  the name/title; 404 and 409 log a rejection without the name.

## 5. Other API tests

- **K1 (Kestrel)** `POST /Person` + `POST /AstronautDuty` for `Aoife O'Brien Jr.` -> `Location` OriginalString exactly
  `/AstronautDuty?name=Aoife%20O%27Brien%20Jr.`.
- **K2 (Kestrel, Production, unmigrated DB)** GET `?name=Secretname%20Probe` and POST with name `Secretname Probe` ->
  500 with an empty body; body does not contain `no such table`, `SqliteException`, `Secretname`, `Exception`.
- **K3 (Kestrel)** POST body of 4097+ bytes (valid JSON, padded) -> 413, nothing written; 4096 bytes -> not 413.
- **C1/C2 (`AstronautDutyControllerTests`)** Each action passes the request's `CancellationToken` to `IMediator.Send`
  (fake mediator capturing the token, as `PersonControllerTests` C6/C7).
- **R1 (`ResponseShapeTests`)** `GetAstronautDutiesByNameResult` serialises to {person, astronautDuties, success,
  message, responseCode}; items have exactly the six keys (no `person`). Rewrites the existing test.
- **R2 (`ResponseShapeTests`)** `CreateAstronautDutyResult { Id = 9, Name = "x" }` serialises to exactly {id,
  success, message, responseCode} (no `name`). Extends the existing test.
- **V1 (`InputRulesTests`)** `IsValidDutyDate`: 1899-12-31 false, 1900-01-01 true, 2100-12-31T23:59:59 true,
  2101-01-01 false, `DateTime.MinValue` false; `ValidDutyDateAttribute` null valid.

## 6. Mutation map (each mutation must fail at least the listed tests)

| Mutation | Fails |
|---|---|
| Interpolated person lookup in create | S1, S7, CH11 (if via EF) |
| Interpolated SQL in GET | S2, S3, H5 |
| `COLLATE NOCASE` dropped (either handler) | G2, A2, H4, S7 |
| `Normalize` dropped | G2, A2, A15 |
| Duplicate check unscoped / full datetime / case-sensitive | A6+CH6 / A5+CH9 / A5+CH7 |
| 404 <-> 400 <-> 409 swapped; order of checks changed | A3, A4, A13, G5 |
| 201 -> 200, or `Created` without Location | A1, K1 |
| Location unencoded or built from the request name | K1, S5, A2 |
| CancellationToken not passed | C1, C2, H6, CH12 |
| try/catch with `ex.Message` reintroduced | K2, A16 |
| Tie-break dropped | G10, CH8 |
| Date bounds removed | A9, V1 |
| `GetPersonByName` sent again from GET | G1 (no duties) |
| Pre-processor re-registered | A3 (stack-trace 400 instead of 404) |

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
- **D7** 500 and status 0 (network error) -> error is an `HttpErrorResponse` with that status; nothing emitted (never
  `notFound`).
- **D8** 200 with `person: null`, and 200 with `astronautDuties` missing -> error `Malformed duties response`.
- **D9** Sent unmodified: `getDuties('  Elena Vasquez ')` -> `?name=%20%20Elena%20Vasquez%20`; `getDuties('')` ->
  a request with `?name=` is still made.
- `npm test -- --watch=false` and `npm run build` pass; no `any`.
