# Task 001: tests each platform must satisfy

Conventions:
- API tests live in `exercise1/api.Tests` (xUnit). "Fixture" = a `TestDatabase` migrated to latest (`db.Migrate()`), seeded through `CreateContext()` with:
  - Unassigned: `Mira Okafor`, `Kenji Watanabe`
  - `Tomas Lindqvist` CPT / Pilot; `Priya Raman` MAJ / Mission Specialist; `Elena Vasquez` MAJ / Commander; `Desmond Achebe` LTC / RETIRED (each with an `AstronautDetail`; duties optional)
  - Extra rows only where a case says so.
- "Tables intact" = `Person` row count unchanged and `AstronautDetail` row count unchanged after the call.
- Assert names as sets (order is unspecified).
- Build stays warning-free (`dotnet build -warnaserror`); no `!`, no `NoWarn`, no `#pragma`.

## API: GetPeople handler (`GetPeopleQueryTests.cs`)

| Id | Given | When (`GetPeople` with) | Then |
|---|---|---|---|
| P1 | Fixture | no filters | all 6 names; Mira and Kenji have `CurrentRank == ""`, `CurrentDutyTitle == ""`, null dates; `ResponseCode 200`, `Success true` |
| P2 | Fixture | `Name = "ra"`; and `Name = "ELENA"`; and `Name = "okafor"` | {Mira Okafor, Priya Raman}; {Elena Vasquez}; {Mira Okafor} |
| P3 | Fixture | `Rank = "maj"`; `Rank = "MA"`; `Rank = "MAJ "` | {Priya Raman, Elena Vasquez}; empty; {Priya Raman, Elena Vasquez} (trimmed) |
| P4 | Fixture | `DutyTitle = "mission specialist"`; `DutyTitle = "Mission"`; `DutyTitle = "retired"` | {Priya Raman}; empty; {Desmond Achebe} |
| P5 | Fixture | `Rank = "MAJ", DutyTitle = "commander"`; `Name = "priya", Rank = "maj"`; `Name = "elena", DutyTitle = "Pilot"` | {Elena Vasquez}; {Priya Raman}; empty |
| P6 | Fixture | `Name = "Nobody"` | `People` empty (not null), `ResponseCode 200`, `Success true` |
| P7 | Fixture | `Name = "Mira", Rank = "CPT"`; `Rank = ""` (normalised away) ; `Name = "kenji"` | empty; all 6; {Kenji Watanabe} with `""` rank/title |
| P8 | Fixture | `Name = "x' OR '1'='1"`; `Rank = "x' OR '1'='1"`; `DutyTitle = "x' OR '1'='1"`; `Name = "'; DROP TABLE Person; --"` | each returns empty; tables intact |
| P9 | Fixture + people `100% Real`, `Ann_Marie`, `AnnxMarie`, `Back\Slash` | `Name = "%"`; `Name = "_"`; `Name = "\"`; `Name = "n_M"` | {100% Real}; {Ann_Marie}; {Back\Slash}; {Ann_Marie} (not `AnnxMarie`: `_` is literal) |
| P10 | Fixture | `Name = "   "`, `Rank = null`, `DutyTitle = "\t"` | all 6 (whitespace = no filter) |
| P11 | Fixture + person `Élena Test` (assigned or not) | `Name = "Élena"`; `Name = "ÉLENA"`; `Name = "élena"` | {Élena Test}; {Élena Test}; empty (characterises ASCII-only folding; documented) |

## API: GetPersonByName handler (`GetPersonByNameQueryTests.cs`)

| Id | Given | When (`Name =`) | Then |
|---|---|---|---|
| N1 | Fixture | `"Elena Vasquez"` | 200, `Success true`, `Message "Successful"`, person MAJ / Commander / CareerStartDate set |
| N2 | Fixture | `"elena VASQUEZ"` | 200, `Person.Name == "Elena Vasquez"` |
| N3 | Fixture | `"Nobody"` | `ResponseCode 404`, `Success false`, `Message == "Person not found"`, `Person null`; message does not contain `"Nobody"` |
| N4 | Fixture | `"Mira"`; `"%"`; `"_ira Okafor"` | 404 each (no contains, no wildcards) |
| N5 | Fixture | `"x' OR '1'='1"`; `"'; DROP TABLE Person; --"` | 404 each; tables intact |
| N6 | Fixture + person `Aoife O'Brien` | `"Aoife O'Brien"`; `"aoife o'brien"` | 200 with that person (apostrophe safe) |
| N7 | Fixture | `"  Mira Okafor  "` | 200 Mira Okafor (trimmed) |
| N8 | Fixture + extra person `MIRA OKAFOR` (inserted after `Mira Okafor`) | `"MIRA OKAFOR"`; `"Mira Okafor"`; `"mira okafor"` | the `MIRA OKAFOR` row; the `Mira Okafor` row; the `Mira Okafor` row (lowest Id) |
| N9 | Fixture | `"Kenji Watanabe"` | 200, `CurrentRank ""`, `CurrentDutyTitle ""`, null dates (rule 2, shape kept) |
| N10 | Fixture | `""`; `"   "` | 404 each |

## API: input rule (`InputRulesTests.cs`)

Use `new RegularExpressionAttribute(InputRules.AllowedCharactersPattern)` and `new StringLengthAttribute(InputRules.MaxLength)` (`IsValid`), i.e. exactly what the controller declares.

| Id | Given | When | Then |
|---|---|---|---|
| R1 | Every `Name`, `CurrentRank`, `CurrentDutyTitle` and duty `Rank`/`DutyTitle` in `DevelopmentSeedData.People()` | validated | all valid |
| R2 | `Aoife O'Brien`, `Anne-Marie St. John`, `Zoë`, `José`, `2LT`, `RETIRED`, 100 × `a` | validated | valid |
| R3 | 101 × `a` | validated | invalid (length) |
| R4 | `x' OR '1'='1`, `%`, `_`, `\`, `a/b`, `a;b`, `<script>`, `a+b`, `a&b`, `"quoted"`, `a\tb`, `a\nb`, `a\u0000b`, `O’Brien` (U+2019) | validated | invalid (charset) |

## API: controller (`PersonControllerTests.cs`)

Build a `ServiceCollection` with `AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString))` and `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PersonController).Assembly))`; resolve `IMediator` from a scope; `new PersonController(mediator)`.

| Id | Given | When | Then |
|---|---|---|---|
| C1 | Fixture | `GetPeople(null, null, null, ct)` | `ObjectResult.StatusCode 200`, value `GetPeopleResult` with 6 people, `ResponseCode == StatusCode` |
| C2 | Fixture | `GetPeople("ra", null, null, ct)` and `GetPeople(null, "maj", "commander", ct)` | 2 people; {Elena Vasquez} (filters forwarded) |
| C3 | Fixture | `GetPersonByName("mira okafor", ct)` | 200, person Mira Okafor, `ResponseCode 200` |
| C4 | Fixture | `GetPersonByName("Nobody", ct)` | `StatusCode 404`, value `GetPersonByNameResult` with `ResponseCode 404`, `Success false`, `Person null` |
| C5 | `TestDatabase` NOT migrated (no tables) | `GetPeople(...)` and `GetPersonByName("x", ct)` | the call throws (`SqliteException`); no `ObjectResult` with a 500 body is produced |

## API: response shape (`ResponseShapeTests.cs`, add)

| Id | Given | When | Then |
|---|---|---|---|
| S1 | The 404 result from N3 | serialised with `JsonSerializerDefaults.Web` | keys exactly `person, success, message, responseCode`; `person` Null, `success` False, `responseCode` 404 |

Existing tests in `ResponseShapeTests`, `NullPersonGuardTests` and the rest pass unchanged.

## API: HTTP tier H (`PersonHttpTests.cs` with `WebApplicationFactory<Program>` if plan Q2 is approved; otherwise QA executes these with curl and records them)

Factory: Development environment (Swagger), `UseSetting("ConnectionStrings:StarbaseApiDatabase", "Data Source=<per-test TestDatabase path>")`, so the initializer migrates and seeds the 6 Development people into the temp file, never `starbase.db`.

| Id | When | Then |
|---|---|---|
| H1 | `GET /Person?name=<101 a>`; `?rank=<101 a>`; `?dutyTitle=<101 a>` | 400; body has `status 400` and `errors` key `name` / `rank` / `dutyTitle` respectively |
| H2 | `GET /Person?name=%25`, `?name=_`, `?rank=a%2Fb`, `?dutyTitle=%3Cscript%3E` | 400 each; body does not contain the submitted value |
| H3 | `GET /Person?name=x'%20OR%20'1'%3D'1` and `GET /Person/x'%20OR%20'1'%3D'1` | 400 each (Q1 recommendation). If you choose the Q1 alternative, only the path case changes, to 404; the filter case stays 400 |
| H4 | `GET /Person?name=x'%20OR%20'a'%20LIKE%20'a` | 200, `people` empty; `GET /Person` afterwards still 6 |
| H5 | `GET /Person/Mira%20Okafor`; `GET /Person/mira%20okafor` | 200, `person.name == "Mira Okafor"`, `responseCode 200` |
| H6 | `GET /Person/Nobody` | 404, body `responseCode 404`, `success false`, `person null` |
| H7 | `GET /Person?name=&rank=%20` | 200, 6 people |
| H8 | `GET /Person/%20` | 400 |
| H9 | `GET /swagger/v1/swagger.json` | `/Person` get has parameters `name`, `rank`, `dutyTitle` (in query, `maxLength` 100, `pattern` present) and responses `200`, `400`, `500`; `/Person/{name}` get has responses `200`, `400`, `404`, `500` |

## UI (`exercise1/ui/src/app/api/stargate-api.spec.ts`, Vitest + `HttpTestingController`)

Setup: `provideHttpClient()`, `provideHttpClientTesting()`; `afterEach(() => http.verify())`. Match requests with `http.expectOne(...)` on the full URL (urlWithParams) unless stated.

| Id | Given | When | Then |
|---|---|---|---|
| U1 | – | `listPeople()` and `listPeople({})` | one GET each to exactly `/api/Person` (no `?`) |
| U2 | – | `listPeople({ name: 'ra' })` | GET `/api/Person?name=ra` |
| U3 | – | `listPeople({ name: 'ra', rank: 'MAJ', dutyTitle: 'Mission Specialist' })` | one GET to `/api/Person` with `params.get('name') === 'ra'`, `'rank' === 'MAJ'`, `'dutyTitle' === 'Mission Specialist'`, and `params.keys().length === 3` |
| U4 | – | `listPeople({ name: '', rank: '   ', dutyTitle: undefined })` | GET exactly `/api/Person` |
| U5 | – | `listPeople({ rank: ' MAJ ' })` | GET `/api/Person?rank=MAJ` |
| U6 | – | `listPeople({ name: "O'Brien & Co+1#" })` | `urlWithParams === "/api/Person?name=O'Brien%20%26%20Co%2B1%23"` |
| U7 | – | `listPeople({ name: 'zzz' })`, flushed with `people: []` | emits a result whose `people` is `[]` |
| U8 | – | `getPerson('Mira Okafor')`, flushed 200 with a person | GET `/api/Person/Mira%20Okafor`; emits that `PersonAstronaut` |
| U9 | – | `getPerson('Nobody')`, flushed `{ success: false, message: 'Person not found', responseCode: 404, person: null }` with status 404 | emits `null`, completes, error callback NOT called |
| U10 | – | `getPerson("x' OR '1'='1")`, flushed a `ValidationProblemDetails` with status 400 | GET `/api/Person/x'%20OR%20'1'%3D'1`; errors with `HttpErrorResponse` `status 400` and `error.errors.name` defined |
| U11 | – | `getPerson('Mira Okafor')`, flushed status 500 | errors with `HttpErrorResponse` `status 500` |
| U12 | – | `getPerson('a/b?c#d')` | GET `/api/Person/a%2Fb%3Fc%23d` |
| U13 | – | `getPerson('   ')` and `getPerson('')` | emits `null` and completes; `http.expectNone(() => true)` (no request) |
| U14 | – | `getPeople()` | GET exactly `/api/Person` (and `app.spec.ts` passes unchanged) |
| U15 | – | `listPeople({ name: '%' })`, flushed `ValidationProblemDetails` with status 400 | errors with `HttpErrorResponse` `status 400` |

Gates: `npm test -- --watch=false` and `npm run build` pass; no `any`.

## QA (real app, Development seed; see plan section 11)

QA runs plan section 11 end to end, including the tier H cases if they were not automated, the `/AstronautDuty/Nobody` → 404 side effect, the apostrophe round trip via `POST /Person`, and the UI table still loading through the dev proxy.
