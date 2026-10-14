# Task 002: tests each platform must satisfy (revision 1)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, body, persisted state), not implementation.
Notation: `NFD(x)` is the canonical decomposition of x; `<U+XXXX>` is that code point. "Dump" is `TestDatabase.Dump()` (all rows of Person, AstronautDetail and AstronautDuty).
Seed means `DevelopmentSeedData.People()` (6 people: Mira Okafor (no assignment), Kenji Watanabe (no assignment), Tomas Lindqvist, Priya Raman, Elena Vasquez, Desmond Achebe).

## Existing tests to adapt (no behaviour change)

- E1 `TestDatabase` gains `AllMigrations` (ordered `GetMigrations()`), `const MakeCareerStartDateRequired = "20261014212120_MakeCareerStartDateRequired"`, `CreateContext(params IInterceptor[] interceptors)`, `PersonNameCollation()` (returns `coll` from `pragma_index_xinfo('IX_Person_Name')` for column `Name`, or null) and `HasUniquePersonNameIndex()`.
- E2 `MigrationTests` line 38 and `DevelopmentDatabaseInitializerTests` (lines 35-36, 51, 64): replace `[InitialCreate, LatestMigration]`, `Count == 2` and `[LatestMigration]` with `AllMigrations`, `AllMigrations.Count` and `AllMigrations.Skip(1)`. All other assertions unchanged; all existing tests stay green.
- E3 `InputRulesTests`: all existing valid/invalid cases keep their outcome under the new pattern.

## API: shared rule (`InputRulesTests.cs`)

- R10 Given `NFD("Renée")`, When `Normalize`, Then the result equals `"Renée"` (NFC, U+00E9) and `IsValidName` is true.
- R11 Given `"  Ada \t"`, When `Normalize`, Then `"Ada"`.
- R12 Given `"\uD800abc"` (lone surrogate), When `Normalize`, Then no exception and `IsValidName(result)` is false.
- R13 Given each of `Ada<U+3164>`, `<U+3164>`, `<U+115F><U+1160>`, `Ada<U+FFA0>`, `A<U+034F>da`, `Ada<U+FE0F>`, `Ada<U+FE00>`, `Ada<U+17B4>`, `Ada<U+180B>`, `Ada<U+180F>`, When `IsValidName(Normalize(x))`, Then false (one InlineData per code point in the invisible set).
- R14 Given `<U+0301>Ada` (leading mark), `1<U+0301>`, `Ada <U+0301>`, `O'<U+0301>`, Then invalid.
- R15 Given `q<U+0301><U+0302><U+0303>` (`q` has no precomposed form, so NFC keeps 3 marks), Then valid; Given `q<U+0301><U+0302><U+0303><U+0304>`, Then invalid; Given `a<U+0301><U+0302><U+0303><U+0304>` (NFC composes `á`, leaving 3 marks), Then valid (validation runs on the NFC form).
- R16 Given Thai `กิ่`, Devanagari `नमस्ते`, `Zoë`, `O'Brien-Smith Jr.`, `José`, Then valid.
- R17 Given `"Ada\n"`, When `Regex.IsMatch(x, AllowedCharactersPattern)` (raw, not the attribute), Then false.
- R18 Given 100 letters padded with 3 spaces each side, Then `IsValidName(Normalize(x))` true; Given 101 letters, Then false; Given `""` or `"   "`, Then false.
- R19 `ValidNameAttribute`: null -> valid; `42` (int) -> invalid; `" Ada "` -> valid; `NFD("Renée")` -> valid; `"<script>"` -> invalid; the error message equals `AllowedCharactersMessage`.

## API: handler (`UpdatePersonHandlerTests.cs`, file DB via `TestDatabase`, migrated to latest, seeded)

- H1 Given Mira (id m), When `UpdatePerson{m, "Mira Okafor-Lindqvist"}`, Then 200, `Success` true, `Person.PersonId == m`, `Person.Name == "Mira Okafor-Lindqvist"`, `CurrentRank == ""`, dates null; the stored name equals it; all other Person rows unchanged.
- H2 Given Tomas (assigned), When renamed, Then the AstronautDetail and AstronautDuty parts of Dump are identical before and after, and `Person.CurrentRank`/`CurrentDutyTitle`/`CareerStartDate` equal the pre-rename values.
- H3 Given `"  Tomas  Lindqvist-Berg \t"`, Then stored `"Tomas  Lindqvist-Berg"` (only surrounding whitespace removed).
- H4 Given `NFD("Renée Ödegaard")`, Then stored value equals the NFC string (compare code units).
- H5 Given Mira, When renamed to `"Mira Okafor"` (own exact name), Then 200 and Dump unchanged.
- H6 Given Mira, When renamed to `"MIRA okafor"`, Then 200 and stored `"MIRA okafor"`.
- H7 Given the same request sent twice, Then both results have identical status, message and Person, and Dump after the second equals Dump after the first.
- H8 Given id 999999, Then 404, `Success` false, `Message == "Person not found"`, `Person` null; Dump unchanged.
- H9 Given Kenji, When renamed to `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR  "`, Then each 409, `Message == "A person with this name already exists"`, `Person` null; Dump unchanged.
- H10 Given Mira renamed to `"Renée"` (NFC), When Kenji is renamed to `NFD("Renée")` and to `NFD("rEnée")`, Then each 409 (NFC plus ASCII-case folding). Given Kenji renamed to `"RENÉE"` (non-ASCII `É` differs from `é`), Then 200 (documented ASCII-only limit).
- H11 (database constraint path) Given a context created with a `SaveChangesInterceptor` whose `SavingChangesAsync` (first call only) executes `UPDATE Person SET Name = 'Zed Unique' WHERE Id = <Priya>` on a separate `SqliteConnection`, When Kenji is renamed to `"zed unique"`, Then the handler returns 409 (no exception), and `SELECT COUNT(*) FROM Person WHERE Name = 'zed unique' COLLATE NOCASE` is 1 and that row is Priya.
- H12 Given a `DbUpdateException` whose inner `SqliteException` has extended code 787 (FK) or no inner exception, Then `SqliteErrors.IsUniqueViolation` is false; with 2067 it is true.
- H13 Given an invalid name passed directly to the handler (`"<x>"`), Then `ArgumentException` and Dump unchanged.

## API: HTTP (`PersonUpdateHttpTests.cs`, `WebApplicationFactory<Program>`, Development, temp DB migrated + seeded, as `PersonHttpTests`)

- P1 Given Mira, When `PUT /Person/{m}` `{"name":"Mira Okafor-Lindqvist"}`, Then 200, JSON exactly `{success:true, message:"Successful", responseCode:200, person:{personId:m, name:"Mira Okafor-Lindqvist", currentRank:"", currentDutyTitle:"", careerStartDate:null, careerEndDate:null}}`, and `GET /Person?name=Lindqvist` lists her.
- P2 Unknown id 999999 -> 404 body `{success:false, message:"Person not found", responseCode:404, person:null}`.
- P3 Kenji -> `"mira okafor"` and `"  MIRA OKAFOR "` -> 409 body `{success:false, message:"A person with this name already exists", responseCode:409, person:null}`; GET shows names unchanged.
- P4 Same PUT twice -> both 200 with byte-identical bodies.
- P5 Own casing (`"MIRA OKAFOR"` for Mira) -> 200.
- P6 400 `ValidationProblemDetails` (`status` 400, `errors` non-empty), DB unchanged, and the body does not contain the submitted value, for each: `{"name":""}`, `{"name":"   "}`, `{"name":null}`, `{}`, 101 letters, `"<script>alert(1)</script>"`, `"x' OR '1'='1"`, `"Robert'); DROP TABLE Person;--"`, `"a\tb"`, `"Ada<U+3164>"`, `"<U+0301>Ada"`, `"Ada<U+200B>"`. For name-rule failures an `errors` key equals `name` ignoring case.
- P7 Body shape failures -> 400: empty body with `Content-Type: application/json`; `"Ada"` (bare string); `{"name":123}`; `[]`.
- P8 `personId` failures -> 400 with an `errors` key equal to `personId` ignoring case and no echo: `/Person/0`, `/Person/-1`, `/Person/abc`, `/Person/99999999999`.
- P9 `Content-Type: text/plain` -> 415.
- P10 Hostile but valid: `{"name":"O'Brien"}` -> 200 and stored verbatim (parameterised SQL); Person count unchanged.
- P11 (concurrency) Given the 6 seeded people, When 6 PUTs run in parallel (`Task.WhenAll`), person i renamed to a distinct-case variant of `"Zed Unique"` (`"Zed Unique"`, `"zed unique"`, `"ZED UNIQUE"`, `"Zed unique"`, `"zed Unique"`, `" Zed Unique "`), Then exactly one 200 and five 409, no 500, and exactly one row matches `Zed Unique` under NOCASE. Repeat the scenario 5 times in the test (fresh names each time).
- P12 Swagger `/swagger/v1/swagger.json` has `put` under `/Person/{personId}` with responses 200, 400, 404, 409.
- P13 (read side, contract change for 001) After renaming Mira to `"Renée Ödegaard"` (NFC), `GET /Person?name=` + URL-encoded `NFD("Renée")` lists her; `GET /Person?name=` + `Ada%E3%85%A4` (U+3164) -> 400.
- P14 Renaming does not change `GET /Person` entries of any other person (compare full lists minus the renamed entry).

## API: migration (`PersonNameIndexMigrationTests.cs`)

- M1 Given a fresh DB, When migrated, Then `HasUniquePersonNameIndex()` and `PersonNameCollation() == "NOCASE"`, `AppliedMigrations() == AllMigrations`, and inserting `ADA` after `Ada` raw-SQL fails with SQLite error 19.
- M2 Given a DB at `MakeCareerStartDateRequired` with Persons `" Ada "`, `"Bob\t"`, `"Cy"` and an AstronautDetail + 2 AstronautDuty rows for Bob, When migrated, Then names are `Ada`, `Bob`, `Cy` with the same Ids, and the AstronautDetail/AstronautDuty dump is identical.
- M3 Given `Ada` and `ada` at the 003 state, When migrated, Then a `SqliteException` (directly or as inner) whose message contains `AddUniquePersonNameIndex`, `No data was changed` and `back up`; Dump identical to before; `AppliedMigrations() == [InitialCreate, MakeCareerStartDateRequired]`; no index `IX_Person_Name`; Person DDL has no `NOCASE`; no `ef_temp_%`/`__guard_%` objects.
- M4 Same as M3 with `Ada` and `"  ADA\t"` (whitespace + case); additionally the padded name is still stored untrimmed.
- M5 Given a migrated DB with rows, When migrated down to `MakeCareerStartDateRequired`, Then no `IX_Person_Name`, no NOCASE in DDL, rows kept; inserting `Ada` and `ada` then succeeds; after deleting `ada`, migrating up again succeeds.
- M6 Given the 003 state with Seed inserted, When migrated, Then success and 6 people remain.
- M7 Existing `Model_has_no_pending_changes_against_the_migration_chain` passes.
- M8 (initializer) Given a Development DB at the 003 state with `Ada`/`ada`, When `InitializeAsync`, Then `InvalidOperationException` mentioning `migration`, no seed rows, history unchanged.

## UI (`exercise1/ui/src/app/api/stargate-api.spec.ts`, Vitest + `HttpTestingController`)

- U1 Given `updatePerson(7, '  Ada ')`, Then exactly one request `PUT /api/Person/7` with body `{ name: '  Ada ' }` (deep equal, no other keys).
- U2 Given a 200 `UpdatePersonResult` with person P, Then emits `{ status: 'updated', person: P }` once and completes.
- U3 Given 404 with the 404 envelope, Then emits `{ status: 'notFound' }`.
- U4 Given 409 with the 409 envelope, Then emits `{ status: 'conflict' }`.
- U5 Given 400 with a `ValidationProblemDetails` whose `errors` is `{ Name: ['invalid'] }`, Then emits `{ status: 'invalid', errors: { Name: ['invalid'] } }`.
- U6 Given 400 with body `null` or a string, Then emits `{ status: 'invalid', errors: {} }`.
- U7 Given 500 (and, separately, status 0 network error), Then the observable errors with an `HttpErrorResponse` of that status.
- U8 Given 200 with `person: null`, Then the observable errors with an `Error` whose message is `Malformed update response`.
- `httpTestingController.verify()` after each test; no `any`.

## QA (real app, Development, seeded DB): see plan section 9 "QA on the real API".
