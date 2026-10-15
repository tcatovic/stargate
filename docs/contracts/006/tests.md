# Task 006: tests each platform must satisfy (revision 3)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted state), not implementation. Test IDs are unique within a file, not across files; where an ID repeats in another file (for example `M1`, `P19`) this document writes `File.ID`.

**Revision 3 changes** (the review round; this file now matches the suite merged at `998036f`)
- Removed as weak or duplicate: `InputRulesTests` R13 and R21 (their rows moved into `NonAsciiNames.All` and R11), R25's source-text checks (R25 is now behaviour only), `GetPeopleQueryTests.F5` (source scan), `CreatePersonHandlerTests.H3` (covered by X1 and R4b), `PersonUpdateHttpTests.P13` (replaced by P20), and duplicate non-ASCII rows in `R19` and `UpdatePersonHandlerTests.H13`.
- Strengthened no-echo checks: `ProblemAssert` (new shared helper, below). X1 (both files), PUT P6 and POST P5 now assert the exact `errors.name` literal and scan the decoded JSON.
- New: R27, R28 (punctuation-only names), `PersonCreateHttpTests.P18` (100-character boundary over POST), `PersonCreateHttpTests.P19` (letter or digit required), F6, F7 (punctuation-only filters), `PersonUpdateHttpTests.P20` (legacy non-ASCII row), `M1` in both HTTP files (parser messages), `PersonControllerTests` C6 and C7 (controller passes the CancellationToken).
- New rows: R24 (`a.`, `O'Brien`, `A-1`, `. a`, `7`), H8 and H13 (`..`), `InvalidNameBodies` (`.`, `..`, `'`, `-`, `. -`), `NonAsciiNames.All` (3 rows from the deleted R13 and R25 lists).
- Documented tests that already existed but were missing here: `CreatePersonHandlerTests` H8b and H11b.
- The mutation table is updated.
- Every non-ASCII value in this file is now written as a C# escape or `<U+XXXX>`; the file is ASCII-only.

**Revision 2 changes**
- New: section A (existing tests to delete, rewrite or keep).
- New: sections C1 and C2.
- C3 tests rewritten to ASCII names: H2, H3, H4, H5, P3, P4, K1.
- H8 has lost its raw-bound row.
- New tests: P17 and X1.
- The mutation table has been extended.

**Notation**
- `<U+XXXX>` is that code point; `"\u00E9"` style values are C# literals.
- "Dump" is `TestDatabase.Dump()`.
- "Seed" is `DevelopmentSeedData.People()`: Mira Okafor, Kenji Watanabe, Tomas Lindqvist, Priya Raman, Elena Vasquez and Desmond Achebe.
- "RuleMessage" is the literal `"Use only English letters (A-Z), digits, spaces, hyphens, apostrophes and periods, with at least one letter or digit (at most 100 characters)."` and "RequiredMessage" is `"The Name field is required."`. Tests compare against these literals (`ProblemAssert`), never against the code's constants.

**"Envelope409"** means all of the following:
- The body keys are exactly `id`, `message`, `responseCode` and `success`.
- `id` is JSON null.
- `success` is false.
- `message` is `"A person with this name already exists"`.
- `responseCode` is 409.
- There is no `Location` header.

**`ProblemAssert`** (`exercise1/api.Tests/ProblemAssert.cs`), the shared 400 assertions:
- `HasOnlyNameError(problem, key, expected)`: the `errors` entry whose key equals `key` ignoring case exists exactly once and its messages are exactly `[expected]`.
- `NameRejected(problem, requestBody)`: for a body whose `name` is a non-blank string, `errors.name` is exactly `[RuleMessage]`; for a missing or null `name`, exactly `[RequiredMessage]`; for an empty or whitespace-only string, exactly `[RequiredMessage, RuleMessage]`. Then `NothingDecodedContains`.
- `NothingDecodedContains(problem, values...)`: the body is parsed, so JSON escapes are decoded, and no property name or string value under `errors`, `title` or `detail` contains any submitted value or its trimmed form. A value that is itself a substring of RuleMessage or RequiredMessage (a lone `.` or `-`) cannot be distinguished and is skipped.
- `NoParserInternals(problem)`: no key or string value anywhere in the body contains `StargateAPI`, `System.`, `Path`, `LineNumber`, `BytePosition` or `converted`.

**NONASCII**, the shared hostile list (`NonAsciiNames.All` in `InputRulesTests.cs`, 27 values), as C# literals:

```
"Zo\u00EB", "Jos\u00E9", "\u00C9mile", "Ren\u00E9e", "Rene\u0301e", "\uFF21da", "\u0410da", "\u0391da", "Ada \U0001F680",
"\u00A0Ada", "Ada\u00A0", "\u3000Ada", "\u0085Ada", "Ada\u2003", "A\u00A0da", "O\u2019Brien", "\u212Aate",
"\u017Fam", "\u0663", "\u0E01\u0E34", "Ada\u200B", "Ada\u3164", "Ada\u180E", "\u180EAda", "A\u034Fda", "Ada\uFE0F", "\uFF11"
```

**PUNCT**, punctuation-only values (rev. 3): `"."`, `".."`, `"'"`, `"-"`, `". -"`, `" ' . - "`.

## A. Existing tests (commit set noted per row; final state after the review round)

| File | Test | Action | Set |
| --- | --- | --- | --- |
| InputRulesTests | R10, R13b, R13c, R13d, R14, R15, R16, R22, R23 | **Delete** | C1 |
| InputRulesTests | R13, R21 | Rewritten in C1, then **deleted** in the review round. R13's rows are in NONASCII; R21's are in R11's non-ASCII theory | C1, review |
| InputRulesTests | R2 theory | Rewrite: remove the `Zo\u00EB` and `Jos\u00E9` rows | C1 |
| InputRulesTests | R4 theory | Keep all rows. R4b (below) covers NONASCII | C1 |
| InputRulesTests | R11 | Split: `R11_NormalizeTrimsSurroundingAsciiWhitespaceOnly` (`"  Ada \t"`, `"\r\nAda\r\n"`, `"\v\fAda\f\v"`, `"Tomas  Lindqvist"`) and `R11_NonAsciiWhitespaceIsNotTrimmed` (each of `<U+00A0>Ada`, `Ada<U+3000>`, `<U+0085>Ada`, `Ada<U+2003>` comes back from `Normalize` unchanged) | C1 |
| InputRulesTests | R18 | Retitle to `R18_LengthIsMeasuredAfterTrimming`. The body is unchanged | C1 |
| InputRulesTests | R19 | `null` valid, `42` invalid, `" Ada "` valid, `"<script>"` invalid, `FormatErrorMessage("Name")` equals RuleMessage. The non-ASCII rows were removed in review (covered by R4b) | C1, review |
| InputRulesTests | R20 | Retitle to `R20_SeedNamesAreValidAndAlreadyNormalised`. The body is unchanged | C1 |
| InputRulesTests | R1, R2 (100 chars), R3, R12, R17, R18b | Keep | - |
| UpdatePersonHandlerTests | H4 (NFD stored as NFC), H10 (NFD and non-ASCII case variants) | **Delete** | C1 |
| UpdatePersonHandlerTests | H13 theory | Final rows: `"<x>"`, `""`, `"   "`, `"Ada\u3164"`, `"Ada\u180E"`, `"\u180EAda"`, `".."`. The C1 rows `"Ren\u00E9e"`, `"Rene\u0301e"`, `"\u00A0Ada"` were removed in review as duplicates of R4b | C1, review |
| UpdatePersonHandlerTests | H18 | Rename to `H18_AnOverlongName_IsRejected`. Drop the raw-bound comment. The 801 and 800 assertions stay | C1 |
| UpdatePersonHandlerTests | `RenameOnAnotherConnection`, `ThrowOnSave` | Move to `TestDoubles.cs` (internal sealed). Rename the first to `SqlOnAnotherConnectionBeforeSave` | C3 |
| UpdatePersonHandlerTests | H1-H3, H5-H9, H11-H12b, H14-H17 | Keep. H3 and H9 tab rows still trim | - |
| PersonUpdateHttpTests | P13 | Rewritten in C1, then **replaced** in review by P20 (C1 section) | C1, review |
| PersonUpdateHttpTests | P6 | `InvalidNameBodies` gains PUNCT rows `.`, `..`, `'`, `-`, `. -`; assertions become `ProblemAssert.NameRejected` | review |
| PersonUpdateHttpTests | P1 | `Get("/Person?name=Lindqvist")` becomes `Get("/Person?name=mira%20okafor-lindqvist")`. Assert that `Names(list)` equals exactly `["Mira Okafor-Lindqvist"]` | C2 |
| PersonUpdateHttpTests | P2-P5, P7-P12, P14-P19 | Keep. The P6 Unicode rows and P19 (U+180E at an edge) are still valid 400 cases | - |
| GetPeopleQueryTests | P11 | **Delete** | C1 |
| GetPeopleQueryTests | P2 | Rewrite as `P2_NameIsCaseInsensitiveExact`. Expected results: `"ra"` gives `[]`, `"ELENA"` gives `[]`, `"okafor"` gives `[]`, `"elena vasquez"` gives `[Elena Vasquez]`, `"MIRA OKAFOR"` gives `[Mira Okafor]` | C2 |
| GetPeopleQueryTests | P5 | `"priya"` becomes `"priya raman"`, and `"elena"` becomes `"elena vasquez"` (still empty with Pilot) | C2 |
| GetPeopleQueryTests | P7 | `"Mira"` becomes `"mira okafor"`, and `"kenji"` becomes `"kenji watanabe"` | C2 |
| GetPeopleQueryTests | P9 | Rewrite as `P9_WildcardCharactersAreLiteralInAnExactMatch`, using the same fixture. Expected results: `"%"` gives `[]`, `"_"` gives `[]`, `"n_M"` gives `[]`, `"ann_marie"` gives `[Ann_Marie]` (not AnnxMarie), `"Back\\Slash"` gives `[Back\Slash]`, `"100% real"` gives `[100% Real]` | C2 |
| GetPeopleQueryTests | P12 | `" elena "` becomes `" elena vasquez "` | C2 |
| GetPeopleQueryTests | P1, P3, P4, P6, P7b, P8, P10, CancelledToken | Keep | - |
| PersonControllerTests | C1-C5 | Keep; C6 and C7 added (C3 section) | review |
| PersonHttpTests, GetPersonByNameQueryTests, ResponseShapeTests, NullPersonGuardTests, SeedDataRulesTests, PersonHttpProductionTests, PersonNameIndexMigrationTests (M1-M13), MigrationTests, DevelopmentDatabaseInitializerTests, StarbaseDatabaseTests | all pre-existing tests | Keep unchanged | - |

## C1. New tests: ASCII rule and letter-or-digit rule

- R4b: Given each NONASCII value, When `IsValidName(Normalize(x))`, the raw `RegularExpressionAttribute(AllowedCharactersPattern)`, or `ValidNameAttribute` is applied, Then each is false.
- R24: Given `"Aoife O'Brien"`, `"Anne-Marie St. John"`, `"2LT"`, `"RETIRED"`, `"O'Brien-Smith Jr."`, `"a."`, `"O'Brien"`, `"A-1"`, `". a"`, `"7"` and (separate fact) 100 `Z`, Then valid.
- R25: Given `"\u212Aate"` (Kelvin sign) and `"\u017Fam"` (long s), Then invalid; this guards against `IgnoreCase`. Given `"\u0663"` and `"\uFF11"` (non-ASCII digits), Then invalid; this guards against `\d`. Behaviour only: the test does not read the pattern text.
- R26: `ValidNameAttribute` with `new string('a', 5000)`: invalid, and no exception (there is no raw bound). With `"   " + new string('a', 100) + "\t"`: valid. With 5000 spaces + 101 `a`: invalid. With 5000 spaces + `"Ada"`: valid.
- R27 (rev. 3): Given each PUNCT value, When `IsValidName(Normalize(x))` and the raw `RegularExpressionAttribute`, Then both are false.
- R28 (rev. 3): 100 periods is invalid, 99 periods + `"\u00E9"` is invalid, 99 hyphens + `"a"` is valid, and all three together take under 1 second (the lookahead stays linear).
- X1 (`NameRuleHttpTests.cs`, Development, seeded): for each NONASCII value `v`, each of the following returns 400:
  - `GET /Person?name=` + `Uri.EscapeDataString(v)`
  - `GET /Person/` + `Uri.EscapeDataString(v)`
  - `PUT /Person/{mira}` with `{"name": v}`

  In every case `ProblemAssert.HasOnlyNameError(body, "name", RuleMessage)` and `NothingDecodedContains(body, v, escaped v)`, and Dump is unchanged.
- X1 (`PersonCreateHttpTests.cs`): `POST /Person` with `{"name": v}` returns 400 with the same two `ProblemAssert` checks, no `Location`, and Dump unchanged.
- `PersonUpdateHttpTests.P20` (rev. 3; replaces P13): Given a legacy row `"Jos\u00E9"` inserted directly into the database, When `GET /Person?name=Jos%C3%A9`, Then 400; When `GET /Person`, Then the list contains `"Jos\u00E9"`; When `PUT /Person/{its id}` with `{"name":"Jose"}`, Then 200 and `person.name` is `"Jose"`, and `GET /Person?name=jose` returns exactly `["Jose"]`.

## C2. New tests: exact filter (`PersonHttpTests`)

- F1: Given the seed, When `GET /Person?name=ra`, `?name=elena`, `?name=Okafor` or `?name=Mira%20Okafo`, Then 200, `success` true and an empty `people` array.
- F2: When `?name=Elena%20Vasquez`, `?name=ELENA%20VASQUEZ` or `?name=%20%20elena%20vasquez%20`, Then 200 and `people` is exactly one entry: `Elena Vasquez`.
- F3: When `?name=priya%20raman&rank=MAJ`, Then exactly Priya Raman. When `?name=priya%20raman&rank=CPT`, Then empty. When `?name=mira%20okafor&dutyTitle=Pilot`, Then empty (rule 2).
- F4: Swagger `paths./Person.get` parameter `name`:
  - its `description` contains "exact" and not "contains";
  - each of the three parameters' `schema.pattern` equals the literal `^(?=[ .'-]*[A-Za-z0-9])[A-Za-z0-9 .'-]+\z`.
- F5: removed in the review round (it scanned source text; F1 already fails for a contains implementation).
- F6 (rev. 3): `GET /Person?name=..`, `?name='`, `?rank=.` and `?dutyTitle=-` (each value escaped) return 400 with `status` 400 and an `errors` entry under that parameter's key.
- F7 (rev. 3): `GET /Person?name=a.` returns 200 with an empty `people` array (a letter plus punctuation passes the rule).

## C3. POST /Person

### Shared test doubles

- E1: as in section A.
- E2: add `CountingCommandInterceptor : DbCommandInterceptor` to `TestDoubles.cs`. It records each executed command's text.

### Handler (`CreatePersonHandlerTests.cs`, seeded `TestDatabase`, `CapturingLogger<CreatePersonHandler>`)

- H1: When `"Ada Lovelace"`, Then:
  - 201, `Success` true, `Message` "Successful";
  - `Id` is at least 1 and equals the row's Id;
  - `Name` is "Ada Lovelace";
  - the Person count is 7.
- H2: When `"  Ada \t"`, `"\r\nAda\r\n"` or `" Ada  Lovelace "` (each on a fresh database), Then 201 and the stored name is `"Ada"`, `"Ada"` or `"Ada  Lovelace"` respectively.
- H3: removed in the review round (non-ASCII is covered by R4b and both X1 tests).
- H4: Given a `CountingCommandInterceptor`, When `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR "` or `"Mira Okafor\t"`, Then:
  - 409, with `Id` and `Name` null and the Envelope409 message;
  - Dump is unchanged;
  - no `INSERT` command was executed.
- H5: When `"ada lovelace"` after `"Ada Lovelace"` was created, Then 409 and there is still one row.
- H6 (race): Given `SqlOnAnotherConnectionBeforeSave(conn, INSERT INTO "Person" ("Name") VALUES ('Zed Unique'))`, When `"zed unique"`, Then:
  - the interceptor ran;
  - 409 with `Id` null;
  - exactly one row matches `'zed unique' COLLATE NOCASE`, named `Zed Unique`;
  - the count is 7.
- H7: Given `ThrowOnSave(new DbUpdateException("fk", new SqliteException("fk", 19, 787)))`, Then the `DbUpdateException` propagates and the count is 6.
- H8: Given `""`, `"   "`, `"<x>"`, `".."` (rev. 3) or `"Secretvalue<x>"`, Then `ArgumentException`, its message does not contain the value, and Dump is unchanged.
- H8b: Given 101 or 5000 `a`, Then `ArgumentException` whose message does not contain `"aaaa"`, and Dump is unchanged.
- H9 (rule 2): `Dump("AstronautDetail", "AstronautDuty")` is identical before and after a create, and there is no `AstronautDetail` or `AstronautDuty` row for the new id.
- H10: The log has exactly two Information entries: one with "Created person {id}" and one with "Create person rejected: name in use". No entry contains "Ada", "Lovelace" or "Mira".
- H11: With a cancelled token, Then `OperationCanceledException` and the count is 6.
- H11b: With a token cancelled during `SaveChanges` (`CancelDuringSave`), Then `OperationCanceledException`: the token reaches the save.

### Controller (`PersonControllerTests.cs`, a capturing `IMediator`)

- C6 (rev. 3): `CreatePerson(new CreatePersonRequest { Name = "Ada" }, token)` sends the command with exactly that `token`.
- C7 (rev. 3): `UpdatePerson(1, new UpdatePersonRequest { Name = "Ada" }, token)` sends the command with exactly that `token`.

### HTTP (`PersonCreateHttpTests.cs`, Development, seeded temp DB, `AllowAutoRedirect = false`)

- P1: `{"name":"Ada Lovelace"}` returns:
  - 201;
  - body keys exactly `id`, `message`, `responseCode`, `success` (no `name`);
  - `success` true, `message` "Successful", `responseCode` 201, and `id` equal to the database Id;
  - `Location` equal to `/Person?name=Ada%20Lovelace`.
- P2: `{"name":"   Ada Lovelace  "}` returns 201, the stored name is "Ada Lovelace", and `Location` is `/Person?name=Ada%20Lovelace`.
- P3: `{"name":"Aoife O'Brien-Smith Jr."}` returns 201 with `Location` equal to `/Person?name=Aoife%20O%27Brien-Smith%20Jr.`. Every header character is printable ASCII (0x21-0x7E).
- P4 (duplicates): each of `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR "` and `"Mira Okafor\t"` returns 409 with Envelope409, and Dump is unchanged.
- P5: each body in `PersonUpdateHttpTests.InvalidNameBodies` (including the PUNCT rows) returns:
  - 400 with `status` 400;
  - `ProblemAssert.NameRejected` (exact `errors.name` messages and no decoded echo);
  - no `Location` header;
  - Dump unchanged.
- P6 (body shape, `application/json`): each of (`""`, -), (`"\"Tokenabc\""`, Tokenabc), (`{"name":4242424242}`, 4242424242), (`[]`, -), (`{"name":`, -) and (`{"name":"Tokenxyz",`, Tokenxyz) returns:
  - 400 with non-empty `errors`;
  - a body that does not contain the token;
  - a body without `"name":`;
  - Dump unchanged.
- `PersonCreateHttpTests.M1` (rev. 3): each of `[]`, `{"name":4242}`, `"Ada"`, `{"name":"Tokenxyz` and `{"name":` sent as `application/json` returns 400 with `status` 400, a non-empty `errors` object, `ProblemAssert.NoParserInternals`, and Dump unchanged.
- P7a: `text/plain` or `application/xml` with a valid body returns 415 and Dump is unchanged.
- P7b: a bare `"Ada"` sent as `text/plain` returns 415; as `application/json` it returns 400.
- P7c: no content and no Content-Type returns 415. The tests check the status only.
- P8: a name of 3000 `a` returns 400. The body does not contain 50 consecutive `a`, and Dump is unchanged.
- P9: NONASCII through POST is covered by `PersonCreateHttpTests.X1`.
- P10: `{"name":"Kai Extra","id":999,"personId":5,"role":"admin"}` returns 201, `id` is not 999 or 5, and there is one "Kai Extra" row.
- P11 (concurrency): run 5 rounds. Each round sends 6 parallel POSTs of `Zed Unique{n}`, `zed unique{n}`, `ZED UNIQUE{n}`, `Zed unique{n}`, `zed Unique{n}` and ` Zed Unique{n} `. Then:
  - exactly one 201 and five 409;
  - nothing at 500 or above;
  - one row matching NOCASE;
  - the count is 6 + n.
- P12 (Swagger): `paths./Person.post` has summary "Create a person" and responses exactly 201, 400, 409, 415 and 500. Its `requestBody` `application/json` schema is `CreatePersonRequest`, which has `name`. The `CreatePersonResult` component has `id` and **no** `name`.
- P13: `{"name":"O'Brien"}` returns 201, the stored name is `O'Brien`, and `Location` is `/Person?name=O%27Brien`.
- P14 (rule 2): `Dump("AstronautDetail", "AstronautDuty")` is identical before and after P1.
- P15: posting the same name twice gives 201, then 409 (Envelope409), with one row.
- P16: `{"name":"Mira Okafor<script>"}` returns 400, not 409.
- P17 (Location round trip): for each of `"Ada Lovelace"` and `"Aoife O'Brien Jr."`, after the 201, a `GET` of the `Location` value returns 200 with `people` of exactly one entry. That entry's `personId` equals `id`, and its `name` is the stored name. `currentRank` and `currentDutyTitle` are `""`, and `careerStartDate` and `careerEndDate` are null.
- `PersonCreateHttpTests.P18` (rev. 3): 100 `a` returns 201; a space + 100 `b` + a space returns 201 and the stored name has length 100; 101 `c` returns 400; the count is 8.
- `PersonCreateHttpTests.P19` (rev. 3): `"a."`, `"O'Brien"` and `"A-1"` each return 201 (count 7); `".."`, `"'"` and `". -"` each return 400 (count 6).
- X1: see C1.

### PUT additions (`PersonUpdateHttpTests.cs`)

- `PersonUpdateHttpTests.M1` (rev. 3): the same five malformed bodies as `PersonCreateHttpTests.M1`, sent with PUT to Mira's id, return 400 with non-empty `errors`, `ProblemAssert.NoParserInternals`, and Dump unchanged.
- P6: see section A (PUNCT rows, `ProblemAssert.NameRejected`).
- P20: see C1.

### Real Kestrel (`PersonCreateKestrelTests.cs`, `factory.UseKestrel(0); factory.StartServer();`)

- K1: Development, seeded. `{"name":"Aoife O'Brien Jr."}` returns 201, `Location` is exactly `/Person?name=Aoife%20O%27Brien%20Jr.`, and the row is stored once.
- K2: Production, never-migrated DB. `{"name":"Secretname Probe"}` returns 500, and the body contains none of `no such table`, `SqliteException`, `Person`, `Secretname` or `Exception`.
- K3: Development, seeded. A body over 4096 bytes returns 413, and the count is 6.

## C4. UI (`stargate-api.spec.ts`, `describe('createPerson')`)

- U1: `createPerson(' Ada ')` makes exactly one request: `POST /api/Person` with body deep-equal to `{ name: ' Ada ' }`.
- U2: `createPerson('')` still sends a POST with `{ name: '' }`.
- U3: a 201 with `{ id: 7, success: true, message: 'Successful', responseCode: 201 }` emits `{ status: 'created', id: 7 }` once, then completes.
- U4: a 201 whose `id` is null, missing, `0`, `-1`, `1.5` or `'7'` errors with `Error('Malformed create response')`.
- U5: 409 emits `{ status: 'conflict' }`.
- U6: 400 with a `ValidationProblemDetails` body emits `{ status: 'invalid', errors: body.errors }`.
- U7: 400 with a null body, a string body or a body without `errors` emits `{ status: 'invalid', errors: {} }`.
- U8: 415, 413, 500 and status 0 each error with an `HttpErrorResponse` of that status.
- U9: no `any`, and the exports match `types.ts`. `npm run build` passes.

The UI is unaffected by revision 3: it does no client-side validation, and a 400 for a punctuation-only name or malformed JSON is still `{ status: 'invalid', errors }`.

## Mutation targets (each must fail at least one test)

| Mutation | Caught by |
| --- | --- |
| Pattern uses `\p{L}` or `\w` | R4b, X1 |
| Pattern uses `\d` | R25 (`\u0663`, `\uFF11`), R4b |
| `RegexOptions.IgnoreCase` added | R25 (Kelvin sign, long s), R4b |
| Letter-or-digit lookahead removed (punctuation-only accepted) | R27, `PersonCreateHttpTests.P19`, F6, H8 and H13 (`..`), PUT P6 and POST P5 (PUNCT rows) |
| Lookahead rewritten so it backtracks (for example `(?=.*[A-Za-z0-9])` with a nested quantifier) | R28 (time bound); a plain `.*` lookahead is linear and is not caught, which is acceptable |
| `Normalize` uses `Trim()` (Unicode set) | R11 (`NonAsciiWhitespaceIsNotTrimmed`), R4b and X1 (edge NBSP, U+3000, U+0085, U+2003 rows) |
| `Normalize` trims only `' '` | R11, H2, H4 (tab row) |
| NFC re-added before validation | R25 and R4b (`\u212Aate`: NFC maps U+212A to `K`, so the name would become valid) |
| Length checked before trimming | R18, R26, `PersonCreateHttpTests.P18` |
| `$` instead of `\z` | R17 |
| Message text changed | R19, X1, PUT P6, POST P5 (exact literal) |
| `AllowInputFormatterExceptionMessages = false` removed | `M1` in both HTTP files |
| Filter back to `LIKE` contains | P2, F1, P17 |
| `COLLATE NOCASE` dropped from the name filter | P2 (`MIRA OKAFOR`), F2 |
| Pre-check deleted or its NOCASE dropped | H4 (no-INSERT assertion) |
| `catch (DbUpdateException)` without the filter | H7 |
| Unique-violation catch removed | H6, P11 |
| 200 instead of `Created` | P1 |
| `Uri.EscapeDataString` removed | P3, K1, P17 |
| Location built from the raw input | P2 |
| `[JsonIgnore]` removed | P1, P12 |
| `[Consumes]` removed | P7a, P7b |
| `[RequestSizeLimit]` removed | K3 |
| try/catch returning `ex.Message` re-added | K2 |
| An `AstronautDetail` is inserted | H9, P14 |
| The name is logged | H10 |
| The handler does not pass the token to the save | H11b |
| The controller does not pass the token to the mediator | C6 (POST), C7 (PUT) |
| UI trims before sending | U1 |
| UI maps 409 to `invalid`, or swallows 500 | U5, U8 |

## Regression and gates

- After each commit set (C1, C2, C3) and after the review round, the whole API suite is green. `dotnet build -warnaserror` passes, and `dotnet ef migrations has-pending-model-changes` reports no changes.
- After C4, `npm test -- --watch=false` and `npm run build` pass.
