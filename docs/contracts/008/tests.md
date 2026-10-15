# Task 008: tests each platform must satisfy (revision 1, amendment 1)

> **Amendment 1 (after merge; re-approval needed).** Behaviour is unchanged. This brings the text in line with the
> merged code and tests:
> 1. Section 5 grep gate reworded. UI-3 must contain the literal `getPerson`, so the old gate could never pass. Now
>    `GetPersonByNameResult` must appear nowhere under `exercise1/ui/src`, and `getPerson` may appear only in
>    `*.spec.ts`.
> 2. Section 1 adds two existing tests that called the removed route and were repaired: `PersonUpdateHttpTests.P15`
>    and `PersonResolutionPreIndexHttpTests.GetDuties_AndGetPerson_ResolveTheSamePerson` (renamed). They are removed
>    from "keep unchanged".
> 3. Section 6 corrects the base column for HEAD. On BASE, HEAD was already 405 with `Allow: GET, PUT`, never
>    200/404/400. Q1p and section 6 now state that `PUT /Person/abc` returns 415 without a body and 400 with a JSON
>    body, on both builds.
> 4. Sections 2-3 list the tests that exist after the review round: Q3k `ra`, Q3m, Q5g-Q5k, the Q7 injection row and
>    shape test, Q6l merged into Q6k, and the Q8 4000-character test asserting 400. New section 7 is the mutation map
>    (10 mutations run by api-dev).
> 5. "Known, deliberately accepted" notes added to Q6k (non-ASCII whitespace only means no filter) and Q8 (the loop
>    accepts 200 or 400 by design).

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
| `PersonHttpTests.H1` | Keep; additionally assert Exact key `{<parameter>}` with NameRule only (merged as `H1_TooLongFilter_Is400_WithOnlyTheSharedNameRuleMessage`) |
| `PersonHttpTests.H2b` | Rename `H2b_EdgeNewlines_AreTrimmed_LikeWrites`; delete the two `/Person/...` rows; the four query rows now expect **200** Envelope200 with `people` = [] (value `abc`) |
| `PersonHttpTests.H3` | Query URL only (drop the `/Person/x'...` URL) |
| `PersonHttpTests.H5b` | `GET /Person?name=aoife%20o'brien` -> 200, `people` = exactly [`Aoife O'Brien`] |
| `PersonHttpTests.H9` | Keep the `/Person` assertions; replace the `/Person/{name}` block with Q9b assertions |
| `NameRuleHttpTests.X1` | Remove the `GET /Person/{escaped}` row; the other two rows unchanged |
| `PersonUpdateHttpTests.P15_AnUnrouteableMethodOrPath_IsNotAffected` | **(Amendment 1)** Rewrite: `GET /Person/Mira%20Okafor` now expects **405** (was 200); `DELETE /Person/1` still 405. Comment says the GET route was removed in task 008 |
| `PersonResolutionPreIndexHttpTests.GetDuties_AndGetPerson_ResolveTheSamePerson` (Theory `Bob`/`bob`/`BOB`) | **(Amendment 1)** Rename `GetDuties_PicksTheExactCaseRow_WhileGetPeopleListsEveryCaseVariant`. The `GET /AstronautDuty?name=` assertions are unchanged (exact-case row first, then lowest Id: ids 9/4/4). The `GET /Person/{name}` call becomes `GET /Person?name=<name>`: 200 and `people[].personId` sorted = exactly `[4, 9]` (this fixture has no unique index, so the filtered list shows both case variants) |
| Everything else (GetPeopleQueryTests, H2, H2c, H2d, H4, H7, H10, F1-F4, F6, F7, other ResponseShape tests, PersonUpdate* except P15, PersonCreate*, AstronautDuty*, PersonResolutionPreIndexHttpTests except the Theory above, PersonHttpProductionTests, migration/seed/initializer tests) | Keep unchanged; must stay green |

## 2. API: new HTTP tests (`PersonQueryHttpTests.cs`)

### Q1 Removed route (Theory)
Rows (path): `/Person/Elena%20Vasquez`, `/Person/elena%20vasquez`, `/Person/Nobody`, `/Person/1`, `/Person/%20`,
`/Person/a%2Fb`, `/Person/x'%20OR%20'1'%3D'1`.
- Given the seeded DB, When GET path, Then status 405, the `Allow` content header values are exactly [`PUT`], the body
  is the empty string, and DB unchanged.
- **Q1h** Given the same, When `HEAD /Person/Elena%20Vasquez`, Then 405 and `Allow` = [`PUT`]. (HEAD was already
  405 on BASE, with `Allow: GET, PUT`. Only the `Allow` value changes.)
- **Q1p** Given the same, When `PUT /Person/abc` with `{"name":"Zed"}` (application/json), Then 400 and DB unchanged
  (regression: PUT binding unchanged; no route constraint added). Note: `PUT /Person/abc` with NO body and no
  Content-Type is 415 on both BASE and HEAD (`[Consumes("application/json")]`), so the JSON body is required for
  this test to reach model binding.

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
| Q3g | `name=Nobody` | {} ; body exactly: `{"people":[],"success":true,"message":"Successful","responseCode":200}` |
| Q3h | `name=Elena%20Vasquez&rank=MAJ&dutyTitle=Commander` | {Elena Vasquez} |
| Q3i | `name=Elena%20Vasquez&rank=MAJ&dutyTitle=Pilot` | {} |
| Q3j | `rank=MAJ&dutyTitle=commander` | {Elena Vasquez} |
| Q3k | `name=Elena` / `name=Vasquez` / `name=lena%20Vasquez` / `name=Elena%20Vasques` / `name=ra` | {} (exact, never contains/prefix/suffix) |
| Q3l | `rank=MA` / `dutyTitle=Command` | {} |
| Q3m | (none) | 6 items, each with exactly the six PersonAstronaut members |
- Each row: 200, Envelope200. Q3b: Mira's item has exactly the six PersonAstronaut members, `personId` 1,
  `currentRank` "" and `currentDutyTitle` "" and null dates.

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
- **Q5g** (Theory, added in review) each of `?query=&name=%25`, `?query[0]=x&name=%25`, `?query.foo=1&name=%25`,
  `?QUERY=x&name=%25`, `?query=x&Name=%25`, `?query.rank=MAJ&name=%25` -> 400, Exact key {name}, No echo `%`,
  DB unchanged.
- **Q5h** (added in review) `?rank=%25&query.rank=MAJ` -> 400, Exact key {rank}, No echo.
- **Q5i** (added in review) `?query.name=%25` -> 200 All6 (not an alias, even for an invalid value: it is never
  bound and never validated).
- **Q5j** (Theory, added in review) `?name[0]=Elena%20Vasquez`, `?name[0]=%25` (indexed keys are a different key
  and are ignored), `?name=&name=Elena%20Vasquez` (an empty FIRST value wins and means no filter) -> 200 All6.
- **Q5k** (Theory, added in review) odd encodings, each 400 Exact key {name} with No echo of the decoded value:
  `?name=%00` (value `\0`), `?name=%FF` (decoded to U+FFFD), `?name=Elena%20Vasquez,Nobody` (comma-joined, one
  value, comma invalid), `?name=%25&name=Elena%20Vasquez` (repeated, invalid first value wins).

### Q6 Trimming is the write rule
| ID | Query | Expected |
|---|---|---|
| Q6 (Theory, rows a-e) | `name=%09Elena%20Vasquez` / `name=Elena%20Vasquez%0A` / `name=%0D%0AElena%20Vasquez%0B%0C` | 200 {Elena Vasquez} |
| | `dutyTitle=%09retired%20` | 200 {Desmond Achebe} |
| | `rank=%20maj%09` | 200 {Priya Raman, Elena Vasquez} |
| Q6f | `name=%20` + 100 x `a` | 200 {} (100 after trim) |
| Q6g | `name=` 100 x `a` + `%09%09%09` | 200 {} |
| Q6h | `name=%20` + 101 x `a` + `%20` | 400 Exact key {name} |
| Q6i | `dutyTitle=a%09b` | 400 Exact key {dutyTitle} (inner tab still invalid) |
| Q6j | `name=%C2%A0Elena%20Vasquez` | 400 Exact key {name} (NBSP is not trimmed) |
| Q6k (Theory) | `name=%C2%A0`, `rank=%E3%80%80` (non-ASCII whitespace only), `name=%09&rank=%0A&dutyTitle=%20` (former Q6l) | 200 All6 |

**Known, deliberately accepted (Q6k).** A value made only of NON-ASCII whitespace (`?name=%C2%A0`, `?rank=%E3%80%80`)
is bound to null by MVC (`ConvertEmptyStringToNull` uses `string.IsNullOrWhiteSpace`, which is Unicode-aware). Null
means "no filter", so the result is 200 with everyone. That contradicts the `InputRules` summary comment
(`api/Business/Validation/InputRules.cs:10`: "any non-ASCII character, including non-ASCII whitespace at an edge, is
invalid"). The comment holds for a value that also contains other characters (Q6j is 400), but not for a value that
is nothing but non-ASCII whitespace. This is the documented behaviour, tested as intended. It returns the same
public list as `GET /Person`, exposes nothing extra, and matches BASE.

### Q7 400 cases: shape, key, message, no echo
Theory rows (query -> Exact key): `name=%25` -> {name}; `name=_` -> {name}; `rank=a%2Fb` -> {rank};
`dutyTitle=%3Cscript%3E` -> {dutyTitle}; `name=..` -> {name}; `name=%C3%89mile` -> {name};
`name=x'%20OR%20'1'%3D'1` -> {name}.
Facts: `name=` 101 x `a` -> {name}; `name=` 101 x `%25` -> {name} (exactly ONE message);
`name=%25&rank=%25&dutyTitle=%25` -> {name, rank, dutyTitle}; `name=Elena%20Vasquez&rank=%25` -> {rank}.
- Then: 400, `status` 400, `traceId` a non-empty string, Exact key, No echo for every submitted value, DB unchanged.
- **Q7 shape** (`Q7_TheProblemBody_HasTheDocumentedShape`): `?name=%25` -> Content-Type `application/problem+json`,
  top-level members exactly {errors, status, title, traceId, type}, `title` "One or more validation errors occurred.".

### Q8 Hostile values are data
- Theory rows: `name=x'%20OR%20'a'%20LIKE%20'a`, `name=%09x'%20OR%20'a'%20LIKE%20'a%0A`, `rank=MAJ'--`,
  `dutyTitle=Commander'%20OR%20'1`, `name=Robert'.%20DROP%20TABLE%20Person`.
  Then: status is 200 with `people` = [] OR 400; never 500; afterwards `GET /Person` -> All6 and DB unchanged.
  **By design** the loop accepts either 200 or 400 for each row: the point is "never widens, never 500, never
  writes", not which side of the name rule each string falls on (the exact 400s are pinned in Q7).
- Fact `Q8_A4000CharacterValue_Is400_NotA500`: `name=` 4000 x `a` (TestServer) -> 400, Exact key {name}, No echo,
  DB unchanged.
- A `%`/`_` never widens results (Q7 rows are 400).

### Q9 Swagger
- **Q9a** `GET /swagger/v1/swagger.json` -> `/Person.get.parameters` names are exactly {name, rank, dutyTitle}
  (ordinal), each `in: query`, not required, schema `type: string`, `maxLength: 100`, `pattern` =
  `^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z` verbatim, NO `minLength`, description verbatim from data.md;
  responses exactly {200, 400, 500}; no parameter name contains `.` (so none is `query.*`).
- **Q9b** `paths` has no `/Person/{name}`; `/Person/{personId}` has exactly one operation, `put`;
  `components.schemas` has no name starting with `GetPersonBy`.
- **Q9c** `/AstronautDuty.get` parameter `name` schema has `maxLength: 100` and the same pattern; still required.
- **Q9d** `CreatePersonRequest.properties.name` has no `maxLength` and no `pattern` (request bodies unchanged).

## 3. API: other new tests

- **Q1k** (`PersonCreateKestrelTests`, real Kestrel, Development, migrated): `GET /Person/Elena%20Vasquez` -> 405,
  `Allow` = [`PUT`], body "".
- **C8** (`PersonControllerTests`, `CapturingMediator` extended to record the request object): Given
  `var q = new GetPeople { Name = "a", Rank = "b", DutyTitle = "c" }`, When `GetPeople(q, ct)`, Then the mediator
  received the same instance (`Assert.Same`) and the same token.
- **V1** (`GetPeopleValidationTests`, `Validator.TryValidateObject(obj, ctx, results, validateAllProperties: true)`):
  for each property alone set to `%`, exactly one result whose `MemberNames` = [that property name] and
  `ErrorMessage` = NameRule.
- **V2** (three tests) all three null -> valid; each property alone set to `"\t Elena Vasquez \n"` -> valid, and 100 x
  `a` padded with spaces -> valid; 101 x `a` -> invalid.
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
- Gates: `npm test -- --watch=false` and `npm run build` pass, and both of these print nothing (Amendment 1: UI-3
  necessarily contains the literal `getPerson`, so specs are excluded for that identifier only):
  - `grep -rn "GetPersonByNameResult" exercise1/ui/src`
  - `grep -rn --exclude='*.spec.ts' "getPerson\b" exercise1/ui/src`

## 6. QA: BASE (e950203) vs HEAD on a brand-new seeded Development DB

Same status and same body (traceId removed; compare `people` as name lists and full items) for:
`/Person`, `?name=Elena%20Vasquez`, `?name=elena%20vasquez`, `?name=%20%20ELENA%20VASQUEZ%20`, `?name=ra`,
`?name=Nobody`, `?rank=maj`, `?dutyTitle=commander`, `?rank=MAJ&dutyTitle=Commander`, `?name=priya%20raman&rank=MAJ`,
`?name=priya%20raman&rank=CPT`, `?name=Mira%20Okafor`, `?name=Mira%20Okafor&rank=MAJ`, `?Name=Elena%20Vasquez`,
`?NAME=Elena%20Vasquez`, `?DUTYTITLE=retired`, `?name=Elena%20Vasquez&name=Nobody`, `?name=Nobody&name=Elena%20Vasquez`,
`?foo=bar&name=Elena%20Vasquez`, `?query=x&name=Elena%20Vasquez`, `?name=`, `?name=%20`, `?dutyTitle=%09`, `?name`,
`?name=%25`, `?name=_`, `?dutyTitle=a%09b`, `?name=%C3%A9lena`, `?name=%C2%A0Elena%20Vasquez`, `?name=%C2%A0`,
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
| `GET /Person/Elena%20Vasquez`, `/Person/Nobody`, `/Person/1`, `/Person/%20`, `/Person/a%2Fb` | 200 / 404 / 404 / 400 / 400 with JSON | 405, `Allow: PUT`, empty body |
| `HEAD` on the same paths (Amendment 1: corrected) | 405, `Allow: GET, PUT`, empty body (`[HttpGet]` never matched HEAD; there was no `[HttpHead]`) | 405, `Allow: PUT`, empty body (only the `Allow` value changes) |

Not a difference (Amendment 1): `PUT /Person/abc` with no body is 415 on both builds; with `{"name":"Zed"}`
(application/json) it is 400 on both builds.

Also: swagger.json diff limited to the items in openapi.yaml's header; UI through the dev proxy (`/api/Person`,
`/api/Person?rank=maj`, `/api/Person?name=elena%20vasquez` identical to direct; `/api/Person/Elena%20Vasquez` 405);
regression of POST /Person (201 + Location resolves to exactly that person), PUT /Person/{id} (200/404/409/400,
`PUT /Person/abc` with a JSON body 400), GET /AstronautDuty?name= (200/404/400), POST /AstronautDuty (201/404/409/400).

## 7. Mutation map (Amendment 1)

api-dev ran these 10 mutations against the merged tests and reported each one turned the suite red. The "caught by"
column lists the tests that, from reading them, assert the behaviour each mutation breaks. It is not a recorded
failure log.

| # | Mutation | Caught by |
|---|---|---|
| M1 | Remove `Name = ""` from the action parameter (plain `[FromQuery]`) | Q5a, Q5b, Q5c, Q5g, Q5h (prefix switch drops filters and validation); Q5d, Q5i (`query.name` becomes an alias) |
| M2 | Drop `[FromQuery]` from the action parameter (`[ApiController]` infers `[FromBody]`) | Q3a and every other GET /Person 200 test (415/400 instead of 200). C8 calls the action directly and is not affected |
| M3 | Remove `[ValidName]` from `Rank` | V1 (Rank), Q7 `rank=a%2Fb`, Q7 several-keys, Q7 only-the-invalid-key, Q5h |
| M4 | Change a wire name (`[FromQuery(Name = ...)]` on a property) | Q2, Q3 rows using that key, Q7 Exact key, Q9a parameter names |
| M5 | Handler match `=` changed to `LIKE` (contains) | F1, Q3k, Q3l |
| M6 | Remove the trim from `ValidNameAttribute` (validate the raw value) | Q6 rows a-e, Q6f, Q6g, H2b; also POST/PUT trimming tests |
| M7 | Unregister `ValidNameParameterFilter` | Q9a (maxLength/pattern), Q9c |
| M8 | Reintroduce `GET /Person/{name}` | Q1, Q1h, Q1k, Q9b, P15 |
| M9 | Controller copies the bound object into a new `GetPeople` and drops a field | C8, Q2/Q3 rows using that field |
| M10 | Remove the trim from the handler's `Normalize` | Q6 rows a-e (validated trimmed, matched raw), F2 (padded name), GetPeopleQueryTests P3 and P12 |
