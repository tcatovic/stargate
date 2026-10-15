# Task 006: tests each platform must satisfy (revision 2)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted state), not implementation.

**Revision 2 changes**
- New: section A (existing tests to delete, rewrite or keep).
- New: sections C1 and C2.
- C3 tests rewritten to ASCII names: H2, H3, H4, H5, P3, P4, K1.
- H8 has lost its raw-bound row.
- New tests: P17 and X1.
- The mutation table has been extended.

**Notation**
- `<U+XXXX>` is that code point.
- "Dump" is `TestDatabase.Dump()`.
- "Seed" is `DevelopmentSeedData.People()`: Mira Okafor, Kenji Watanabe, Tomas Lindqvist, Priya Raman, Elena Vasquez and Desmond Achebe.

**"Envelope409"** means all of the following:
- The body keys are exactly `id`, `message`, `responseCode` and `success`.
- `id` is JSON null.
- `success` is false.
- `message` is `"A person with this name already exists"`.
- `responseCode` is 409.
- There is no `Location` header.

**NONASCII**, the shared hostile list, as C# literals:

```
"Zoë", "José", "Émile", "Renée", "Ａda", "Аda", "Αda", "Ada 🚀",
" Ada", "Ada ", "　Ada", "\u0085Ada", "Ada ", "A da", "O’Brien", "Kate",
"ſam", "٣", "กิ", "Ada​", "Adaㅤ", "Ada᠎", "᠎Ada"
```

## A. Existing tests (commit set noted per row)

| File | Test | Action | Set |
| --- | --- | --- | --- |
| InputRulesTests | R10, R13b, R13c, R13d, R14, R15, R16, R22, R23 | **Delete** | C1 |
| InputRulesTests | R2 theory | Rewrite: remove the `Zoë` and `José` rows | C1 |
| InputRulesTests | R4 theory | Keep all rows. The new R4b (below) covers NONASCII | C1 |
| InputRulesTests | R11 | Rewrite: keep the 3 ASCII rows. Add a theory where each of `<U+00A0>Ada`, `Ada<U+3000>`, `<U+0085>Ada` and `Ada<U+2003>` comes back from `Normalize` unchanged | C1 |
| InputRulesTests | R13 theory | Rewrite as "R13_NonAsciiIsInvalidAnywhere". Rows: `Ada<U+3164>`, `Ada<U+200B>`, `A<U+034F>da`, `Ada<U+FE0F>`, `A<U+180E>da`, `Ada<U+180E>`, `<U+180E>Ada`. Assert `IsValidName(Normalize(x))` is false | C1 |
| InputRulesTests | R18 | Retitle to `R18_LengthIsMeasuredAfterTrimming`. The body is unchanged | C1 |
| InputRulesTests | R19 | Rewrite: `"Renée"` is now **false**. Message equals the new `AllowedCharactersMessage` text. Everything else unchanged | C1 |
| InputRulesTests | R20 | Retitle to `R20_SeedNamesAreValidAndAlreadyNormalised`. The body is unchanged | C1 |
| InputRulesTests | R21 | Rewrite as `R21_NonAsciiWhitespaceIsNotTrimmed_AndIsInvalid`. `Normalize(" Ada ")` and `Normalize("　Ada\u0085")` are unchanged, and both are invalid | C1 |
| InputRulesTests | R1, R2 (100 chars), R3, R12, R17, R18b | Keep | - |
| UpdatePersonHandlerTests | H4 (NFD stored as NFC), H10 (NFD and non-ASCII case variants) | **Delete** | C1 |
| UpdatePersonHandlerTests | H13 theory | Add rows `"Renée"`, `"Renée"`, `" Ada"` | C1 |
| UpdatePersonHandlerTests | H18 | Rename to `H18_AnOverlongName_IsRejected`. Drop the raw-bound comment. The 801 and 800 assertions stay | C1 |
| UpdatePersonHandlerTests | `RenameOnAnotherConnection`, `ThrowOnSave` | Move to `TestDoubles.cs` (internal sealed). Rename the first to `SqlOnAnotherConnectionBeforeSave` | C3 |
| UpdatePersonHandlerTests | H1-H3, H5-H9, H11-H12b, H14-H17 | Keep. H3 and H9 tab rows still trim | - |
| PersonUpdateHttpTests | P13 | Rewrite as `P13_NonAsciiIsRejectedOnPutAndOnTheReadSide`. Each of the following returns 400 and changes nothing: PUT `"Renée Ödegaard"`, `GET /Person?name=Rene%CC%81e`, `GET /Person?name=Ada%E3%85%A4`, `GET /Person/Jos%C3%A9` | C1 |
| PersonUpdateHttpTests | P1 | `Get("/Person?name=Lindqvist")` becomes `Get("/Person?name=mira%20okafor-lindqvist")`. Assert that `Names(list)` equals exactly `["Mira Okafor-Lindqvist"]` | C2 |
| PersonUpdateHttpTests | P2-P12, P14-P19 | Keep. The P6 Unicode rows and P19 are still valid 400 cases | - |
| GetPeopleQueryTests | P11 | **Delete** | C1 |
| GetPeopleQueryTests | P2 | Rewrite as `P2_NameIsCaseInsensitiveExact`. Expected results: `"ra"` gives `[]`, `"ELENA"` gives `[]`, `"okafor"` gives `[]`, `"elena vasquez"` gives `[Elena Vasquez]`, `"MIRA OKAFOR"` gives `[Mira Okafor]` | C2 |
| GetPeopleQueryTests | P5 | `"priya"` becomes `"priya raman"`, and `"elena"` becomes `"elena vasquez"` (still empty with Pilot) | C2 |
| GetPeopleQueryTests | P7 | `"Mira"` becomes `"mira okafor"`, and `"kenji"` becomes `"kenji watanabe"` | C2 |
| GetPeopleQueryTests | P9 | Rewrite as `P9_WildcardCharactersAreLiteralInAnExactMatch`, using the same fixture. Expected results: `"%"` gives `[]`, `"_"` gives `[]`, `"n_M"` gives `[]`, `"ann_marie"` gives `[Ann_Marie]` (not AnnxMarie), `"Back\\Slash"` gives `[Back\Slash]`, `"100% real"` gives `[100% Real]` | C2 |
| GetPeopleQueryTests | P12 | `" elena "` becomes `" elena vasquez "` | C2 |
| GetPeopleQueryTests | P1, P3, P4, P6, P7b, P8, P10, CancelledToken | Keep | - |
| PersonHttpTests, GetPersonByNameQueryTests, PersonControllerTests, ResponseShapeTests, NullPersonGuardTests, SeedDataRulesTests, PersonHttpProductionTests, PersonNameIndexMigrationTests (M1-M13), MigrationTests, DevelopmentDatabaseInitializerTests, StarbaseDatabaseTests | all | Keep unchanged | - |

## C1. New tests: ASCII rule

- R4b: Given each NONASCII value, When `IsValidName(Normalize(x))`, Then false. Also, When the raw `RegularExpressionAttribute(AllowedCharactersPattern)` is applied, Then false.
- R24: Given `"Aoife O'Brien"`, `"Anne-Marie St. John"`, `"2LT"`, `"RETIRED"`, `"O'Brien-Smith Jr."` and 100 letters, Then valid.
- R25: Given `"Kate"` (Kelvin sign) and `"ſam"` (long s), Then invalid. This guards against `IgnoreCase`. Given `"٣"` and `"１"` (non-ASCII digits), Then invalid. This guards against `\d`.
- R26: `ValidNameAttribute` with `new string('a', 5000)`: invalid, and no exception (there is no raw bound). With `"   " + new string('a', 100) + "\t"`: valid.
- X1 (`PersonHttpTests` or a new `NameRuleHttpTests.cs`, Development, seeded): for each NONASCII value `v`, each of the following returns 400:
  - `GET /Person?name=` + `Uri.EscapeDataString(v)`
  - `GET /Person/` + `Uri.EscapeDataString(v)`
  - `PUT /Person/{mira}` with `{"name": v}`
  - `POST /Person` with `{"name": v}` (this request added in C3)

  In every case:
  - The body contains neither `v` nor its escaped form.
  - Dump is unchanged.
  - POST has no `Location` header.

## C2. New tests: exact filter (`PersonHttpTests`)

- F1: Given the seed, When `GET /Person?name=ra`, `?name=elena`, `?name=Okafor` or `?name=Mira%20Okafo`, Then 200, `success` true and an empty `people` array.
- F2: When `?name=Elena%20Vasquez`, `?name=ELENA%20VASQUEZ` or `?name=%20%20elena%20vasquez%20`, Then 200 and `people` is exactly one entry: `Elena Vasquez`.
- F3: When `?name=priya%20raman&rank=MAJ`, Then exactly Priya Raman. When `?name=priya%20raman&rank=CPT`, Then empty. When `?name=mira%20okafor&dutyTitle=Pilot`, Then empty (rule 2).
- F4: Swagger `paths./Person.get` parameter `name`:
  - its `description` contains "exact" and not "contains";
  - each of the three parameters' `schema.pattern` equals `InputRules.AllowedCharactersPattern`.
- F5: The file `exercise1/api/Business/Queries/GetPeople.cs`, read from the source tree via a path relative to the test assembly (the same way existing tests locate the API folder, or by `[CallerFilePath]`), contains neither `LIKE` nor `EscapeLike`. If locating the source is not already done in the suite, skip F5. F1 already fails for a contains implementation.

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
- H3: When `" Ada"`, `"Ada　"`, `"José"` or `"Renée"`, Then `ArgumentException` and Dump is unchanged. Non-ASCII is never trimmed or stored.
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
- H8: Given `""`, `"   "`, `"<x>"`, `new string('a', 101)` or `new string('a', 5000)`, Then `ArgumentException`, its message does not contain the value, and Dump is unchanged.
- H9 (rule 2): `Dump("AstronautDetail", "AstronautDuty")` is identical before and after a create, and there is no `AstronautDetail` row for the new id.
- H10: The log has an Information entry with the new id and one with "Create person rejected: name in use". No entry contains "Ada", "Lovelace" or "Mira".
- H11: With a cancelled token, Then `OperationCanceledException` and the count is 6.

### HTTP (`PersonCreateHttpTests.cs`, Development, seeded temp DB, `AllowAutoRedirect = false`)

- P1: `{"name":"Ada Lovelace"}` returns:
  - 201;
  - body keys exactly `id`, `message`, `responseCode`, `success` (no `name`);
  - `success` true, `message` "Successful", `responseCode` 201, and `id` equal to the database Id;
  - `Location` equal to `/Person?name=Ada%20Lovelace`.
- P2: `{"name":"   Ada Lovelace  "}` returns 201, the stored name is "Ada Lovelace", and `Location` is `/Person?name=Ada%20Lovelace`.
- P3: `{"name":"Aoife O'Brien-Smith Jr."}` returns 201 with `Location` equal to `/Person?name=Aoife%20O%27Brien-Smith%20Jr.`. Every header character is printable ASCII (0x21-0x7E).
- P4 (duplicates): each of `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR "` and `"Mira Okafor\t"` returns 409 with Envelope409, and Dump is unchanged.
- P5: each body in `PersonUpdateHttpTests.InvalidNameBodies` returns:
  - 400 with `status` 400;
  - an `errors` key equal to `name` (case-insensitive);
  - a body that does not contain any non-blank submitted value;
  - no `Location` header;
  - Dump unchanged.
- P6 (body shape, `application/json`): each of (`""`, -), (`"\"Tokenabc\""`, Tokenabc), (`{"name":4242424242}`, 4242424242), (`[]`, -), (`{"name":`, -) and (`{"name":"Tokenxyz",`, Tokenxyz) returns:
  - 400 with non-empty `errors`;
  - a body that does not contain the token;
  - a body without `"name":`;
  - Dump unchanged.
- P7a: `text/plain` or `application/xml` with a valid body returns 415 and Dump is unchanged.
- P7b: a bare `"Ada"` sent as `text/plain` returns 415.
- P7c: no content and no Content-Type returns 415. The tests check the status only.
- P8: a name of 3000 `a` returns 400. The body does not contain 50 consecutive `a`, and Dump is unchanged.
- P9: NONASCII through POST is covered by X1.
- P10: `{"name":"Kai Extra","id":999,"personId":5,"role":"admin"}` returns 201, `id` is not 999 or 5, and there is one "Kai Extra" row.
- P11 (concurrency): run 5 rounds. Each round sends 6 parallel POSTs of `Zed Unique{n}`, `zed unique{n}`, `ZED UNIQUE{n}`, `Zed unique{n}`, `zed Unique{n}` and ` Zed Unique{n} `. Then:
  - exactly one 201 and five 409;
  - nothing at 500 or above;
  - one row matching NOCASE;
  - the count is 6 + n.
- P12 (Swagger): `paths./Person.post` has summary "Create a person" and responses exactly 201, 400, 409, 415 and 500. Its `requestBody` `application/json` schema has `name`. The `CreatePersonResult` component has `id` and **no** `name`.
- P13: `{"name":"O'Brien"}` returns 201, the stored name is `O'Brien`, and `Location` is `/Person?name=O%27Brien`.
- P14 (rule 2): `Dump("AstronautDetail", "AstronautDuty")` is identical before and after P1.
- P15: posting the same name twice gives 201, then 409 (Envelope409), with one row.
- P16: `{"name":"Mira Okafor<script>"}` returns 400, not 409.
- P17 (Location round trip): for each of `"Ada Lovelace"` and `"Aoife O'Brien Jr."`, after the 201, a `GET` of the `Location` value returns 200 with `people` of exactly one entry. That entry's `personId` equals `id`, and its `name` is the stored name. `currentRank` and `currentDutyTitle` are `""`, and `careerStartDate` and `careerEndDate` are null.

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

## Mutation targets (each must fail at least one test)

| Mutation | Caught by |
| --- | --- |
| Pattern uses `\p{L}` or `\w` | R4b, X1 |
| Pattern uses `\d` | R25 |
| `RegexOptions.IgnoreCase` added | R25 (Kelvin, long s) |
| `Normalize` uses `Trim()` (Unicode set) | R11, R21, H3, X1 (NBSP rows) |
| `Normalize` trims only `' '` | R11, H2, H4 (tab row) |
| NFC re-added | R19 (`Renée` must stay invalid), H3 |
| Length checked before trimming | R18, R26 |
| `$` instead of `\z` | R17 |
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
| The token is not passed | H11 |
| UI trims before sending | U1 |
| UI maps 409 to `invalid`, or swallows 500 | U5, U8 |

## Regression and gates

- After each commit set (C1, C2, C3), the whole API suite is green. `dotnet build -warnaserror` passes, and `dotnet ef migrations has-pending-model-changes` reports no changes.
- After C4, `npm test -- --watch=false` and `npm run build` pass.
