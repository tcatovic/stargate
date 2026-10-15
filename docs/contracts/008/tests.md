# Task 008: tests each platform must satisfy (revision 1)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted
state), not implementation. IDs are the prefixes of test method names. Literals are written out in tests, not taken
from production constants (existing `ProblemAssert` style).

Fixture conventions (API):
- HTTP tests: `WebApplicationFactory<Program>`, environment Development, `AllowAutoRedirect = false`, per-test temp
  SQLite from `TestDatabase` migrated and seeded with `DevelopmentSeedData.People()` (ids: Mira Okafor 1 (no
  assignment), Kenji Watanabe 2 (no assignment), Tomas Lindqvist 3 (CPT, Pilot), Priya Raman 4 (MAJ, Mission
  Specialist), Elena Vasquez 5 (MAJ, Commander), Desmond Achebe 6 (LTC, RETIRED)). Same setup as `PersonHttpTests`.
- `All6` = the six names above as a set. "Names(x)" = set of `people[].name`.
- NameRule = `ProblemAssert.NameRuleMessage` (the literal).
- "Envelope200" = `success` true, `message` "Successful", `responseCode` 200, `people` an array.
- "No echo" = `ProblemAssert.NothingDecodedContains(json, value, escaped)`.
- "Exact key" = the `errors` object has exactly the listed property names, compared ORDINALLY (case-sensitive),
  each with exactly `[NameRule]`.
- "DB unchanged" = `TestDatabase.Dump()` before == after.

## 1. API: deletions and rewrites of existing tests

| Test | Action |
|---|---|
| `GetPersonByNameQueryTests.cs` (all 10) | Delete file |
| `ResponseShapeTests.GetPersonByNameResult_HasTheDocumentedShape_AndWritesNullPerson`, `S1_NotFoundResult_FromTheRealHandler_HasTheDocumentedShape` | Delete |
| `PersonControllerTests.C3`, `C4` | Delete |
| `PersonControllerTests.C1`, `C2`, `C5` | Rewrite: call `GetPeople(new GetPeople { ... }, ct)`; C5 keeps only the GetPeople fault assertion |
| `PersonHttpTests.H1b`, `H5`, `H6`, `H8` | Delete (route gone; F2/Q3 cover H5, Q3g covers H6) |
| `PersonHttpTests.H1` | Keep; additionally assert Exact key `{<parameter>}` with NameRule only |
| `PersonHttpTests.H2b` | Rename `H2b_EdgeNewlines_AreTrimmed_LikeWrites`; delete the two `/Person/...` rows; the four query rows now expect **200** Envelope200 with `people` = [] (value `abc`) |
| `PersonHttpTests.H3` | Query URL only (drop the `/Person/x'...` URL) |
| `PersonHttpTests.H5b` | `GET /Person?name=aoife%20o'brien` -> 200, `people` = exactly [`Aoife O'Brien`] |
| `PersonHttpTests.H9` | Keep the `/Person` assertions; replace the `/Person/{name}` block with Q9b assertions |
| `NameRuleHttpTests.X1` | Remove the `GET /Person/{escaped}` row; the other two rows unchanged |
| Everything else (GetPeopleQueryTests, H1, H2, H2c, H2d, H4, H7, H10, F1-F7, other ResponseShape tests, PersonUpdate*, PersonCreate*, AstronautDuty*, PersonHttpProductionTests, migration/seed/initializer tests) | Keep unchanged; must stay green |

## 2. API: new HTTP tests (`PersonQueryHttpTests.cs`)

### Q1 Removed route (Theory)
Rows (path): `/Person/Elena%20Vasquez`, `/Person/elena%20vasquez`, `/Person/Nobody`, `/Person/1`, `/Person/%20`,
`/Person/a%2Fb`, `/Person/x'%20OR%20'1'%3D'1`.
- Given the seeded DB, When GET path, Then status 405, the `Allow` content header values are exactly [`PUT`], the body
  is the empty string, and DB unchanged.
- **Q1h** Given the same, When `HEAD /Person/Elena%20Vasquez`, Then 405 and `Allow` = [`PUT`].
- **Q1p** Given the same, When `PUT /Person/abc` with `{"name":"Zed"}` (application/json), Then 400 (regression: PUT
  binding unchanged; no route constraint added).

### Q2 Parameter-name casing (Theory)
Rows: `?name=Elena%20Vasquez`, `?Name=Elena%20Vasquez`, `?NAME=elena%20vasquez`, `?rank=LTC`, `?RANK=ltc`,
`?dutyTitle=retired`, `?dutytitle=RETIRED`, `?DUTYTITLE=Retired`.
- When GET, Then 200 Envelope200 and Names = {Elena Vasquez} for the name rows, {Desmond Achebe} for the others.

### Q3 Filters alone and combined (Theory unless noted)
| ID | Query | Names |
|---|---|---|
| Q3a | (none) | All6 |
| Q3b | `name=Mira%20Okafor` | {Mira Okafor} (unassigned, rule 2: returned by name alone) |
| Q3c | `name=Mira%20Okafor&rank=MAJ` | {} |
| Q3d | `name=Mira%20Okafor&dutyTitle=Pilot` | {} |
| Q3e | `rank=maj` | {Priya Raman, Elena Vasquez} |
| Q3f | `dutyTitle=mission%20specialist` | {Priya Raman} |
| Q3g | `name=Nobody` | {} ; Envelope200 exactly: `{"people":[],"success":true,"message":"Successful","responseCode":200}` |
| Q3h | `name=Elena%20Vasquez&rank=MAJ&dutyTitle=Commander` | {Elena Vasquez} |
| Q3i | `name=Elena%20Vasquez&rank=MAJ&dutyTitle=Pilot` | {} |
| Q3j | `rank=MAJ&dutyTitle=commander` | {Elena Vasquez} |
| Q3k | `name=Elena` / `name=Vasquez` / `name=lena%20Vasquez` / `name=Elena%20Vasques` | {} (exact, never contains/prefix/suffix) |
| Q3l | `rank=MA` / `dutyTitle=Command` | {} |
- Each row: 200, Envelope200, and every returned item has exactly the six PersonAstronaut members; Mira's item (Q3b)
  has `currentRank` "" and `currentDutyTitle` "" and null dates.

### Q4 Repeated parameter, first value wins
- Q4a `?name=Elena%20Vasquez&name=Nobody` -> {Elena Vasquez}.
- Q4b `?name=Nobody&name=Elena%20Vasquez` -> {}.
- Q4c `?rank=CPT&rank=MAJ` -> {Tomas Lindqvist}.
- Q4d `?name=Elena%20Vasquez&name=%25` -> 200 {Elena Vasquez} (only the first value is bound and validated).

### Q5 Unknown and prefix-like parameters are ignored and never bypass validation
- Q5a `?query=x&name=Elena%20Vasquez` -> 200 {Elena Vasquez}.
- Q5b `?query=x&name=%25` -> 400, Exact key {name}, No echo.
- Q5c `?Query=x&rank=CPT` -> 200 {Tomas Lindqvist}.
- Q5d `?query.name=Elena%20Vasquez` -> 200 All6 (not an alias).
- Q5e `?foo=bar&name=Elena%20Vasquez` and `?people=x&name=Elena%20Vasquez` -> 200 {Elena Vasquez}.
- Q5f `?name` (no `=`) -> 200 All6.

### Q6 Trimming is the write rule (Theory)
| ID | Query | Expected |
|---|---|---|
| Q6a | `name=%09Elena%20Vasquez` | 200 {Elena Vasquez} |
| Q6b | `name=Elena%20Vasquez%0A` | 200 {Elena Vasquez} |
| Q6c | `name=%0D%0AElena%20Vasquez%0B%0C` | 200 {Elena Vasquez} |
| Q6d | `dutyTitle=%09retired%20` | 200 {Desmond Achebe} |
| Q6e | `rank=%20maj%09` | 200 {Priya Raman, Elena Vasquez} |
| Q6f | `name=%20` + 100 x `a` | 200 {} (100 after trim) |
| Q6g | `name=` 100 x `a` + `%09%09%09` | 200 {} |
| Q6h | `name=%20` + 101 x `a` + `%20` | 400 Exact key {name} |
| Q6i | `dutyTitle=a%09b` | 400 Exact key {dutyTitle} (inner tab still invalid) |
| Q6j | `name=%C2%A0Elena%20Vasquez` | 400 Exact key {name} (NBSP is not trimmed) |
| Q6k | `name=%C2%A0` and `rank=%E3%80%80` (whitespace only, non-ASCII) | 200 All6 (bound to null by MVC, unchanged) |
| Q6l | `name=%09&rank=%0A&dutyTitle=%20` | 200 All6 |

### Q7 400 cases: shape, key, message, no echo (Theory)
Rows (query -> Exact key): `name=%25` -> {name}; `name=_` -> {name}; `rank=a%2Fb` -> {rank};
`dutyTitle=%3Cscript%3E` -> {dutyTitle}; `name=..` -> {name}; `name=%C3%89mile` -> {name};
`name=` 101 x `a` -> {name}; `name=` 101 x `%25` -> {name} (exactly ONE message);
`name=%25&rank=%25&dutyTitle=%25` -> {name, rank, dutyTitle}; `name=Elena%20Vasquez&rank=%25` -> {rank}.
- Then: 400, Content-Type `application/problem+json`, `type`/`title`/`status` as in openapi.yaml, `traceId` a
  non-empty string, Exact key, No echo for every submitted value, DB unchanged.

### Q8 Hostile values are data (Theory)
Rows: `name=x'%20OR%20'a'%20LIKE%20'a`, `name=%09x'%20OR%20'a'%20LIKE%20'a%0A`, `rank=MAJ'--`,
`dutyTitle=Commander'%20OR%20'1`, `name=Robert'.%20DROP%20TABLE%20Person`, `name=` 4000 x `a` (TestServer).
- Then: status is 200 with `people` = [] or 400 (Exact key); never 500; afterwards `GET /Person` -> All6 and DB
  unchanged. A `%`/`_` never widens results (Q7 rows are 400).

### Q9 Swagger
- **Q9a** `GET /swagger/v1/swagger.json` -> `/Person.get.parameters` names are exactly {name, rank, dutyTitle}
  (ordinal), each `in: query`, not required, schema `type: string`, `maxLength: 100`, `pattern` =
  `^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z` verbatim, NO `minLength`, description verbatim from data.md;
  responses exactly {200, 400, 500}; no parameter name contains `.` or equals `query`.
- **Q9b** `paths` has no `/Person/{name}`; `/Person/{personId}` has exactly one operation, `put`;
  `components.schemas` has no `GetPersonByNameResult`.
- **Q9c** `/AstronautDuty.get` parameter `name` schema has `maxLength: 100` and the same pattern; still required.
- **Q9d** `POST /Person` request body schema (`CreatePersonRequest`) is unchanged vs main (no added maxLength/pattern).

## 3. API: other new tests

- **Q1k** (`PersonCreateKestrelTests`, real Kestrel, Development, migrated): `GET /Person/Elena%20Vasquez` -> 405,
  `Allow` = [`PUT`], body "".
- **C8** (`PersonControllerTests`, `CapturingMediator` extended to record the request object): Given
  `var q = new GetPeople { Name = "a", Rank = "b", DutyTitle = "c" }`, When `GetPeople(q, ct)`, Then the mediator
  received the same instance (`Assert.Same`) and the same token.
- **V1** (`GetPeopleValidationTests`, `Validator.TryValidateObject(obj, ctx, results, validateAllProperties: true)`):
  for each property alone set to `%`, exactly one result whose `MemberNames` = [that property name] and
  `ErrorMessage` = NameRule.
- **V2** all three null -> valid; each property alone set to `"\t Elena Vasquez \n"` -> valid; 100 x `a` padded with
  spaces -> valid; 101 x `a` -> invalid.
- **V3** `typeof(GetPeople)` implements `IRequest<GetPeopleResult>` and `GetPeopleHandler` handles it via
  `IMediator.Send` in a DI container (no second request type exists for listing people).

## 4. API: build and grep gates (run by the lead and QA)

- `dotnet build -warnaserror` passes; `dotnet test` passes.
- `grep -rnw "GetPersonByName\|GetPersonByNameResult\|GetPersonByNameHandler" exercise1/api exercise1/api.Tests`
  (excluding bin/obj) prints nothing.

## 5. UI (Vitest, `stargate-api.spec.ts`, `HttpTestingController`)

- Delete the `getPerson` describe block (U8-U13 there) and the `GetPersonByNameResult` import.
- Keep listPeople U1-U7, U15 and U14 unchanged.
- **UI-1** Given `listPeople({ name: 'Elena Vasquez' })`, Then exactly one GET with `urlWithParams`
  `/api/Person?name=Elena%20Vasquez`; flushing a body with one person emits that body unchanged.
- **UI-2** Given `listPeople({ name: 'Nobody' })` flushed with `{ people: [], success: true, message: 'Successful',
  responseCode: 200 }`, Then the emitted result has `people` = [] and no error.
- **UI-3** `expect('getPerson' in api).toBe(false)` and `expect((api as unknown as Record<string, unknown>)['getPerson']).toBeUndefined()`.
- **UI-4** A typed check that `PersonFilters` has exactly the keys name, rank, dutyTitle, e.g.
  `const keys: Array<keyof PersonFilters> = ['name', 'rank', 'dutyTitle'];` plus
  `const all: Required<PersonFilters> = { name: 'a', rank: 'b', dutyTitle: 'c' };` passed to `listPeople`, asserting
  exactly 3 params with those keys (extends U3).
- Gates: `npm test -- --watch=false` and `npm run build` pass; `grep -rn "getPerson\b\|GetPersonByNameResult"
  exercise1/ui/src` prints nothing.

## 6. QA: BASE (e950203) vs HEAD on a brand-new seeded Development DB

Same status and same body (traceId removed; compare `people` as name lists and full items) for:
`/Person`, `?name=Elena%20Vasquez`, `?name=elena%20vasquez`, `?name=%20%20ELENA%20VASQUEZ%20`, `?name=ra`,
`?name=Nobody`, `?rank=maj`, `?dutyTitle=commander`, `?rank=MAJ&dutyTitle=Commander`, `?name=priya%20raman&rank=MAJ`,
`?name=priya%20raman&rank=CPT`, `?name=Mira%20Okafor`, `?name=Mira%20Okafor&rank=MAJ`, `?Name=Elena%20Vasquez`,
`?NAME=Elena%20Vasquez`, `?DUTYTITLE=retired`, `?name=Elena%20Vasquez&name=Nobody`, `?name=Nobody&name=Elena%20Vasquez`,
`?foo=bar&name=Elena%20Vasquez`, `?query=x&name=Elena%20Vasquez`, `?name=`, `?name=%20`, `?dutyTitle=%09`, `?name`,
`?name=%25`, `?name=_`, `?dutyTitle=a%09b`, `?name=%C3%A9lena`, `?name=%C2%A0Elena%20Vasquez`,
`?name=x'%20OR%20'1'%3D'1`, `?name=..`, `?rank=a%2Fb`, `?name=x'%20OR%20'a'%20LIKE%20'a`.

Expected differences (exactly these):
| URL | BASE | HEAD |
|---|---|---|
| `?name=` 101 x `a` | 400, message "The field name must be a string with a maximum length of 100." | 400, NameRule only |
| `?name=` 101 x `%25` | 400, two messages | 400, NameRule only |
| `?name=%20` + 100 x `a` | 400 length | 200 [] |
| `?name=Elena%20Vasquez%0A`, `?name=%09Elena%20Vasquez` | 400 NameRule | 200 [Elena Vasquez] |
| `?query=x&name=%25` | 400 | 400 (same; listed to prove no bypass) |
| `?query.name=Elena%20Vasquez` | 200 All6 | 200 All6 (same) |
| `GET`/`HEAD /Person/Elena%20Vasquez`, `/Person/Nobody`, `/Person/1`, `/Person/%20`, `/Person/a%2Fb` | 200 / 404 / 404 / 400 / 400 with JSON | 405, `Allow: PUT`, empty body |

Also: swagger.json diff limited to the items in openapi.yaml's header; UI through the dev proxy (`/api/Person`,
`/api/Person?rank=maj`, `/api/Person?name=elena%20vasquez` identical to direct; `/api/Person/Elena%20Vasquez` 405);
regression of POST /Person (201 + Location resolves to exactly that person), PUT /Person/{id} (200/404/409/400,
`PUT /Person/abc` 400), GET /AstronautDuty?name= (200/404/400), POST /AstronautDuty (201/404/409/400).
