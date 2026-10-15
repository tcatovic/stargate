# Task 002: tests each platform must satisfy (revision 1, Amendment 1 incl. 1b)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, body, persisted state), not implementation.
Notation: `NFD(x)` is the canonical decomposition of x; `<U+XXXX>` is that code point. "Dump" is `TestDatabase.Dump()` (all rows of Person, AstronautDetail and AstronautDuty).
Seed means `DevelopmentSeedData.People()` (6 people: Mira Okafor (no assignment), Kenji Watanabe (no assignment), Tomas Lindqvist, Priya Raman, Elena Vasquez, Desmond Achebe).

Amendment 1 changed these cases:
- R13: covers all 28 code points of the invisible set, plus U+180E at the edges.
- P8: adds `notanumber` and `1.5`, and the no-echo requirement covers the bound message.
- P9: asserts the 415 status only, never the body.
- P1: property order is explicitly not asserted.

Amendment 1b added these cases, to match behaviour added in the review round. They are present in the merged code (`55ae823`):
- R21 - R23
- H16 - H18
- M9 - M13
- P7 (no-echo tokens), P16 - P18

One gap remains: the R13 U+180E **edge** assertion (`Ada<U+180E>`, `<U+180E>Ada`). The merged tests cover only an inner U+180E (`A<U+180E>da`). Closing the gap needs two InlineData entries, not a code change.

## Existing tests to adapt (no behaviour change)

- E1 `TestDatabase` gains `AllMigrations` (ordered `GetMigrations()`), `const MakeCareerStartDateRequired = "20261014212120_MakeCareerStartDateRequired"`, `CreateContext(params IInterceptor[] interceptors)`, `PersonNameCollation()` (returns `coll` from `pragma_index_xinfo('IX_Person_Name')` for column `Name`, or null) and `HasUniquePersonNameIndex()`.
- E2 `MigrationTests` line 38 and `DevelopmentDatabaseInitializerTests` (lines 35-36, 51, 64): replace `[InitialCreate, LatestMigration]`, `Count == 2` and `[LatestMigration]` with `AllMigrations`, `AllMigrations.Count` and `AllMigrations.Skip(1)`. All other assertions unchanged; all existing tests stay green.
- E3 `InputRulesTests`: all existing valid/invalid cases keep their outcome under the new pattern.

## API: shared rule (`InputRulesTests.cs`)

- R10 Given `NFD("Renée")`, When `Normalize`, Then the result equals `"Renée"` (NFC, U+00E9) and `IsValidName` is true.
- R11 Given `"  Ada \t"`, When `Normalize`, Then `"Ada"`.
- R12 Given `"\uD800abc"` (lone surrogate), When `Normalize`, Then no exception and `IsValidName(result)` is false.
- R13 (amended) Given `"Ad" + <cp> + "a"` for each of the 28 code points in the invisible set (data.md table: U+034F, U+115F, U+1160, U+17B4, U+17B5, U+180B-U+180F, U+3164, U+FE00-U+FE0F, U+FFA0), with the list written as numbers independent of the pattern and its count asserted to be 28, When `IsValidName(Normalize(x))`, Then false; and `"Ada"` is true. In addition, Given `Ada<U+3164>`, `<U+3164>`, `<U+115F><U+1160>`, `Ada<U+FFA0>`, `A<U+034F>da`, `Ada<U+FE0F>`, `Ada<U+200B>`, Then false. Given `Ada<U+180E>` or `<U+180E>Ada`, Then `Normalize` leaves the length unchanged (U+180E is not whitespace) and the result is invalid.
- R14 Given `<U+0301>Ada` (leading mark), `1<U+0301>`, `Ada <U+0301>`, `O'<U+0301>`, Then invalid.
- R15 Given `q<U+0301><U+0302><U+0303>` (`q` has no precomposed form, so NFC keeps 3 marks), Then valid; Given `q<U+0301><U+0302><U+0303><U+0304>`, Then invalid; Given `a<U+0301><U+0302><U+0303><U+0304>` (NFC composes `á`, leaving 3 marks), Then valid (validation runs on the NFC form).
- R16 Given Thai `กิ่`, Devanagari `नमस्ते`, `Zoë`, `O'Brien-Smith Jr.`, `José`, Then valid.
- R17 Given `"Ada\n"`, When `Regex.IsMatch(x, AllowedCharactersPattern)` (raw, not the attribute), Then false.
- R18 Given 100 letters padded with 3 spaces each side, Then `IsValidName(Normalize(x))` true; Given 101 letters, Then false; Given `""` or `"   "`, Then false.
- R19 `ValidNameAttribute`: null -> valid; `42` (int) -> invalid; `" Ada "` -> valid; `NFD("Renée")` -> valid; `"<script>"` -> invalid; the error message equals `AllowedCharactersMessage`.
- R21 (1b) Given `<U+00A0>Ada<U+2003>` and `<U+3000>Ada<U+0085>`, When `Normalize`, Then `"Ada"` (Unicode whitespace is trimmed).
- R22 (1b) Given `<U+1D400>` (surrogate pair) alone or after `Ada`, Then invalid (supplementary-plane letters are rejected by design).
- R23 (1b) `ValidNameAttribute`: 801 `a` -> invalid; 801 spaces + `Ada` -> invalid (raw bound applies before trimming); `e<U+0301>` x 100 (200 raw, 100 after NFC) -> valid; x 101 -> invalid; `MaxRawLength == 800`.

## API: handler (`UpdatePersonHandlerTests.cs`, file DB via `TestDatabase`, migrated to latest, seeded)

- H1 Given Mira (id m), When `UpdatePerson{m, "Mira Okafor-Lindqvist"}`, Then 200, `Success` true, `Person.PersonId == m`, `Person.Name == "Mira Okafor-Lindqvist"`, `CurrentRank == ""`, dates null; the stored name equals it; all other Person rows and the AstronautDetail/AstronautDuty dump are unchanged.
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
- H16 (1b, load-to-save race) Given a `SavingChangesAsync` interceptor that deletes Mira on a separate connection, When Mira is renamed, Then 404 `"Person not found"`, `Success` false, `Person` null, no exception; Mira is gone and 5 people remain.
- H17 (1b, save-to-read-back race) Given a `SavedChangesAsync` interceptor that deletes Mira after the save, When Mira is renamed, Then 404 `"Person not found"`, `Person` null (never a 200 with a null person).
- H18 (1b) Given an 801-character raw name passed directly to the handler, Then `ArgumentException` whose message does not contain the value, and Dump unchanged. An 800-character name is also rejected (over 100 after normalising), and Dump is unchanged.

## API: HTTP (`PersonUpdateHttpTests.cs`, `WebApplicationFactory<Program>`, Development, temp DB migrated + seeded, as `PersonHttpTests`)

- P1 Given Mira, When `PUT /Person/{m}` `{"name":"Mira Okafor-Lindqvist"}`, Then 200, JSON exactly `{success:true, message:"Successful", responseCode:200, person:{personId:m, name:"Mira Okafor-Lindqvist", currentRank:"", currentDutyTitle:"", careerStartDate:null, careerEndDate:null}}` (the property sets are compared without regard to order), and `GET /Person?name=Lindqvist` lists her.
- P2 Unknown id 999999 -> 404 body `{success:false, message:"Person not found", responseCode:404, person:null}`.
- P3 Kenji -> `"mira okafor"` and `"  MIRA OKAFOR "` -> 409 body `{success:false, message:"A person with this name already exists", responseCode:409, person:null}`; GET shows names unchanged.
- P4 Same PUT twice -> both 200 with byte-identical bodies.
- P5 Own casing (`"MIRA OKAFOR"` for Mira) -> 200.
- P6 400 `ValidationProblemDetails` (`status` 400, `errors` non-empty), DB unchanged, and the body does not contain the submitted value, for each: `{"name":""}`, `{"name":"   "}`, `{"name":null}`, `{}`, 101 letters, `"<script>alert(1)</script>"`, `"x' OR '1'='1"`, `"Robert'); DROP TABLE Person;--"`, `"a\tb"`, `"Ada<U+3164>"`, `"<U+0301>Ada"`, `"Ada<U+200B>"`. For name-rule failures an `errors` key equals `name` ignoring case.
- P7 (amended in 1b) Body-shape failures -> 400 with non-empty `errors`, DB unchanged, the body does not contain `"name":`, and the submitted token is not echoed. Cases: empty body with `Content-Type: application/json`; `"Tokenabc"` (bare string; `Tokenabc` not echoed); `{"name":4242424242}` (`4242424242` not echoed); `[]`; `{"name":` (truncated); `{"name":"Tokenxyz",` (truncated; `Tokenxyz` not echoed). Error keys are not pinned.
- P8 (amended) `personId` failures -> 400 `ValidationProblemDetails` with an `errors` key equal to `personId` ignoring case, DB unchanged, for `/Person/0`, `/Person/-1`, `/Person/abc`, `/Person/1.5`, `/Person/99999999999`, `/Person/notanumber`. For `99999999999` and `notanumber` the response body must not contain the submitted value. That requires the non-property binding-message accessors in data.md "Program.cs". Message text is not asserted; the expected value for non-binding ids is `"The value is not valid."`.
- P9 (amended) `Content-Type: text/plain` and `Content-Type: application/xml`, each with a valid JSON body -> 415, the body does not contain the submitted name, DB unchanged. The body itself is not asserted: it is empty for these requests, and ProblemDetails when the header is missing.
- P10 Hostile but valid: `{"name":"O'Brien"}` -> 200 and stored verbatim (parameterised SQL); Person count unchanged.
- P11 (concurrency) Given the 6 seeded people, When 6 PUTs run in parallel (`Task.WhenAll`), person i renamed to a distinct-case variant of `"Zed Unique"` (`"Zed Unique"`, `"zed unique"`, `"ZED UNIQUE"`, `"Zed unique"`, `"zed Unique"`, `" Zed Unique "`), Then exactly one 200 and five 409, no 500, and exactly one row matches `Zed Unique` under NOCASE. Repeat the scenario 5 times in the test (fresh names each time).
- P12 Swagger `/swagger/v1/swagger.json` has `put` under `/Person/{personId}` with responses 200, 400, 404, 409 (413 is not listed; see openapi.yaml).
- P13 (read side, contract change for 001) After renaming Mira to `"Renée Ödegaard"` (NFC), `GET /Person?name=` + URL-encoded `NFD("Renée")` lists her; `GET /Person?name=` + `Ada%E3%85%A4` (U+3164) -> 400.
- P14 Renaming does not change `GET /Person` entries of any other person (compare full lists minus the renamed entry).
- P16 (1b) A 3000-character name -> 400 with an `errors` key `name` (ignoring case), the body contains no run of 50 `a`, and the DB is unchanged.
- P17 (1b) A 5000-character name (body over 4096 bytes) is rejected: 413 on Kestrel, or 400 on the in-memory TestServer (which does not enforce `[RequestSizeLimit]`). There is no echo and the DB is unchanged. The 413 itself is a QA check on real Kestrel (plan section 9).
- P18a (1b) `PUT /Person/999999` with `"<script>"` -> 400 with key `name` (validation before lookup, not 404).
- P18b (1b) `PUT /Person/0` with a valid name -> 400 with key `personId` (from `[Range]`, not 404).

## API: migration (`PersonNameIndexMigrationTests.cs`)

- M1 Given a fresh DB, When migrated, Then `HasUniquePersonNameIndex()` and `PersonNameCollation() == "NOCASE"`, `AppliedMigrations() == AllMigrations`, and inserting `ADA` after `Ada` raw-SQL fails with SQLite error 19.
- M2 Given a DB at `MakeCareerStartDateRequired` with Persons `" Ada "`, `"Bob\t"`, `"Cy"` and an AstronautDetail + 2 AstronautDuty rows for Bob, When migrated, Then names are `Ada`, `Bob`, `Cy` with the same Ids, and the AstronautDetail/AstronautDuty dump is identical.
- M3 Given `Ada` and `ada` at the 003 state, When migrated, Then a `SqliteException` (directly or as inner) whose message contains `AddUniquePersonNameIndex`, `No data was changed` and `back up`; Dump identical to before; `AppliedMigrations() == [InitialCreate, MakeCareerStartDateRequired]`; no index `IX_Person_Name`; Person DDL has no `NOCASE`; no `ef_temp_%`/`__guard_%` objects.
- M4 Same as M3 with `Ada` and `"  ADA\t"` (whitespace + case); additionally the padded name is still stored untrimmed.
- M5 Given a migrated DB with rows, When migrated down to `MakeCareerStartDateRequired`, Then no `IX_Person_Name`, no NOCASE in DDL, rows kept; inserting `Ada` and `ada` then succeeds; after deleting `ada`, migrating up again succeeds.
- M6 Given the 003 state with Seed inserted, When migrated, Then success and 6 people remain.
- M7 Existing `Model_has_no_pending_changes_against_the_migration_chain` passes.
- M8 (initializer) Given a Development DB at the 003 state with `Ada`/`ada`, When `InitializeAsync`, Then `InvalidOperationException` mentioning `migration`, no seed rows, history unchanged.
- M9 (1b) `AddUniquePersonNameIndex.TrimCharacterCodes`, parsed and sorted, equals every code point in 0..0xFFFF for which `char.IsWhiteSpace` is true (25). `string.Trim()` removes each of them from both ends of `"x"`.
- M10 (1b) Given, for each of the 25 code points c, names `L<n>`+c, `R<n>`+c+c and c+`B<n>` at the 003 state, When migrated, Then all 75 rows remain, trimmed to `L<n>`, `R<n>` and `B<n>`.
- M11 (1b) Given `<U+3000>Ada` and `Grace<U+00A0>`, When migrated, Then the names are `Ada` and `Grace` with the same Ids.
- M12 (1b) Given `Ada`, `Ada`+c and `Grace` for c in {160, 12288, 8195, 133, 11}, When migrated, Then it aborts with the message, and the dump, history and schema are unchanged (as M3).
- M13 (1b) Given `Ada` and a name that is empty once trimmed (`''`, `char(32)`, `char(12288) || char(9)`), When migrated, Then it aborts and changes nothing (as M3), and the message contains `empty once trimmed`.

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

## QA (real app, Development, seeded DB): see plan section 9 "QA on the real API". It includes the Kestrel 413 check for a body over 4096 bytes.
