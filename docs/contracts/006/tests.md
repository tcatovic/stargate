# Task 006: tests each platform must satisfy (revision 1)

Markdown because agents read and edit it as text. Every test asserts behaviour (status, headers, body, persisted state), not implementation.

Notation:

- `NFD(x)` is the canonical decomposition of x.
- `<U+XXXX>` is that code point.
- "Dump" is `TestDatabase.Dump()`. `Dump("AstronautDetail", "AstronautDuty")` is the astronaut tables only.
- "Seed" is `DevelopmentSeedData.People()`: 6 people, namely Mira Okafor, Kenji Watanabe, Tomas Lindqvist, Priya Raman, Elena Vasquez and Desmond Achebe.
- "Envelope409" means all of the following:
  - The body has exactly the keys `id`, `message`, `responseCode`, `success`.
  - `id` is JSON null.
  - `success` is false.
  - `message` is `"A person with this name already exists"`.
  - `responseCode` is 409.
  - The response has **no** `Location` header.

## Shared test doubles (refactor, no behaviour change)

- E1: Move `ThrowOnSave` and `RenameOnAnotherConnection` from `UpdatePersonHandlerTests.cs` to `TestDoubles.cs` as `internal sealed`.
  - Rename `RenameOnAnotherConnection` to `SqlOnAnotherConnectionBeforeSave`.
  - The existing H11/H12b update tests stay green and unchanged apart from the type name.
- E2: Add `CountingCommandInterceptor : DbCommandInterceptor` to `TestDoubles.cs`. It records the text of every command executed, so tests can assert that no `INSERT INTO "Person"` was issued.

## API: handler (`CreatePersonHandlerTests.cs`, seeded `TestDatabase`, real `StargateContext`, `CapturingLogger<CreatePersonHandler>`)

- H1 Given the seed, When `CreatePerson { Name = "Ada Lovelace" }`, Then:
  - `ResponseCode` 201, `Success` true, `Message` "Successful".
  - `Id` equals the stored row's Id and is >= 1.
  - `Name` is "Ada Lovelace".
  - The Person count is 7.
- H2 Given the seed, When the name is `"  Ada \t"`, `"<U+00A0>Ada<U+3000>"` or `" Ada  Lovelace "`, Then 201 and the stored name is `"Ada"`, `"Ada"` or `"Ada  Lovelace"` respectively (inner whitespace is kept). Run each case on a fresh DB.
- H3 Given the seed, When the name is `NFD("Renée")`, Then 201 and the stored value equals `"Renée"` (U+00E9, length 5).
- H4 Given the seed and a `CountingCommandInterceptor`, When the name is `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR "` or `"<U+00A0>Mira Okafor"`, Then:
  - The result is 409, `Id` null, `Name` null, the message as in Envelope409.
  - Dump is unchanged.
  - The interceptor saw no `INSERT` command (pre-check path).
- H5 Given a person `"Renée"` created through the handler, When the name is `NFD("renée")` (lowercase), Then 409. And When the name is `"RENÉE"`, Then 201. This pins the documented limit that `NOCASE` folds ASCII only.
- H6 Race. Given the seed and `SqlOnAnotherConnectionBeforeSave(conn, INSERT INTO "Person" ("Name") VALUES ('Zed Unique'))`, When the name is `"zed unique"`, Then:
  - The interceptor ran.
  - The result is 409, `Id` null.
  - Exactly one row matches `'zed unique' COLLATE NOCASE`, and it has the intruder's Name `Zed Unique`.
  - The Person count is 7.
- H7 Given `ThrowOnSave(new DbUpdateException("fk", new SqliteException("fk", 19, 787)))`, When a valid new name, Then `DbUpdateException` propagates (it is NOT mapped to 409) and the Person count is 6.
- H8 Given the names `""`, `"   "`, `"<x>"`, `"Ada<U+3164>"`, `new string('a', 101)` and `new string('a', 801)`, When sent to the handler directly, Then `ArgumentException` is thrown. Its message does not contain the value, and Dump is unchanged. For the 801-character input, `Normalize` must not run: a value of 801 spaces plus `"Ada"` is rejected even though it would trim to a valid name.
- H9 Rule 2. Given the seed, When `"Ada Lovelace"` is created, Then `Dump("AstronautDetail", "AstronautDuty")` is identical before and after, and no `AstronautDetail` row has the new PersonId.
- H10 Logging. When H1 and H4 run, Then:
  - The log has an Information entry with the new id.
  - The log has an Information entry "Create person rejected: name in use".
  - No entry's message contains "Ada", "Lovelace" or "Mira".
- H11 Given a cancelled `CancellationToken`, When a valid new name, Then `OperationCanceledException` is thrown and the Person count is 6.

## API: HTTP (`PersonCreateHttpTests.cs`, `WebApplicationFactory<Program>`, Development, seeded temp DB, `AllowAutoRedirect = false`, same `Send` helper as `PersonUpdateHttpTests`)

- P1 When `POST /Person {"name":"Ada Lovelace"}`, Then:
  - 201.
  - The body keys are exactly `id`, `message`, `responseCode`, `success`. The order is not asserted, and there is no `name` key.
  - `success` true, `message` "Successful", `responseCode` 201.
  - `id` equals the DB Id.
  - The `Location` header equals `/Person?name=Ada%20Lovelace`.
  - `GET` of that Location returns 200, and `people` contains an entry whose `personId` equals `id`, `name` is "Ada Lovelace", `currentRank` "", `currentDutyTitle` "", and `careerStartDate` and `careerEndDate` are null.
- P2 When `{"name":"   Ada Lovelace  "}`, Then 201, the stored name is "Ada Lovelace", and `Location` is `/Person?name=Ada%20Lovelace` (built from the stored name).
- P3 When `{"name":"Zoë O'Brien-Smith Jr."}`, Then 201 and `Location` is `/Person?name=Zo%C3%AB%20O%27Brien-Smith%20Jr.`. Every character of the header value is ASCII (< 0x80).
- P4 Duplicates. For each of `"Mira Okafor"`, `"mira okafor"`, `"  MIRA OKAFOR "`, `"Mira Okafor<U+00A0>"`: Then 409 with Envelope409, and Dump is unchanged. Separately: create `"Renée"` (201), then post `NFD("Renée")`, which gives 409 and one row.
- P5 Invalid names. Each body in `PersonUpdateHttpTests.InvalidNameBodies` (same list: `""`, `"   "`, `null`, `{}`, 101 characters, `<script>alert(1)</script>`, `x' OR '1'='1`, `Robert'); DROP TABLE Person;--`, `a\tb`, `Ada<U+3164>`, `<U+0301>Ada`, `Ada<U+200B>`, `Ada<U+115F><U+1160>`, `A<U+034F>da`, `Ada<U+FE0F>`, `q` + 4 marks) gives:
  - 400 with `status` 400.
  - An `errors` key equal to `name`, compared case-insensitively.
  - A non-blank submitted value does not appear in the body.
  - No `Location` header.
  - Dump unchanged.
- P6 Body shape, with Content-Type `application/json`. For each pair (body, token): (`""`, -), (`"\"Tokenabc\""`, Tokenabc) [the pre-006 bare string], (`{"name":4242424242}`, 4242424242), (`[]`, -), (`{"name":`, -), (`{"name":"Tokenxyz",`, Tokenxyz). Then:
  - 400 with non-empty `errors`.
  - The token, when given, is not in the body.
  - The body does not contain `"name":`.
  - Dump unchanged.
- P7 Content type:
  - P7a: `text/plain` or `application/xml` with a valid JSON body gives 415, and Dump is unchanged.
  - P7b: the bare string `"Ada"` as `text/plain` gives 415.
  - P7c: a request with no content and no Content-Type gives 415.
  - Only the status is asserted, never the body.
- P8 A name of 3000 `a` gives 400. The body does not contain 50 consecutive `a`, and Dump is unchanged.
- P9 `Ada<U+180E>` and `<U+180E>Ada` give 400 with a `name` error, and nothing is stored.
- P10 `{"name":"Kai Extra","id":999,"personId":5,"role":"admin"}` gives 201. `id` is not 999 and not 5, and exactly one row is named "Kai Extra".
- P11 Concurrency, 5 rounds. Each round runs 6 parallel POSTs of `Zed Unique{n}`, `zed unique{n}`, `ZED UNIQUE{n}`, `Zed unique{n}`, `zed Unique{n}` and ` Zed Unique{n} ` (`Task.WhenAll`). Then:
  - Exactly one is 201 and five are 409.
  - None is >= 500.
  - Exactly one row matches the name NOCASE.
  - The Person count is 6 + n.
- P12 Swagger. `GET /swagger/v1/swagger.json` gives the following:
  - `paths./Person.post` exists with summary "Create a person".
  - Its response codes are exactly 201, 400, 409, 415, 500.
  - It has a `requestBody` whose `application/json` schema resolves to an object with a `name` property.
  - The `CreatePersonResult` component schema has an `id` property and **no** `name` property.
- P13 `{"name":"O'Brien"}` gives 201, the stored name is exactly `O'Brien`, and `Location` is `/Person?name=O%27Brien`.
- P14 Rule 2 over HTTP. `Dump("AstronautDetail", "AstronautDuty")` is identical before and after P1's POST.
- P15 POSTing `{"name":"Ada Lovelace"}` twice gives 201 and then 409 (Envelope409), with one row.
- P16 Order of checks. `{"name":"Mira Okafor<script>"}` gives 400, not 409.

## API: real Kestrel (`PersonCreateKestrelTests.cs`, `factory.UseKestrel(0); factory.StartServer();`, as in `PersonHttpProductionTests`)

- K1 Development, seeded temp DB. When `{"name":"Zoë Ångström"}`, Then:
  - 201 (not 500).
  - `Location` is `/Person?name=Zo%C3%AB%20%C3%85ngstr%C3%B6m`.
  - The row is stored once.
- K2 Production, never-migrated temp DB. When `{"name":"Secretname Probe"}`, Then:
  - 500.
  - The body contains none of: `no such table`, `SqliteException`, `Person`, `Secretname`, `Exception`.
- K3 Development, seeded. When the body is larger than 4096 bytes (a 5000-character name), Then 413 and the Person count is 6.

## UI (`stargate-api.spec.ts`, `describe('createPerson')`, `HttpTestingController`)

- U1 `createPerson(' Ada ')` sends exactly one request:
  - Method `POST`, URL `/api/Person`.
  - Body deep-equal to `{ name: ' Ada ' }` (unmodified, no other keys).
- U2 `createPerson('')` still sends one POST with `{ name: '' }`.
- U3 A 201 flush with `{ id: 7, success: true, message: 'Successful', responseCode: 201 }` emits `{ status: 'created', id: 7 }` exactly once, then completes.
- U4 A 201 flush with `id` null, missing, `0`, `-1`, `1.5` or `'7'` errors with `Error('Malformed create response')`.
- U5 409 emits `{ status: 'conflict' }` once and completes.
- U6 400 with a `ValidationProblemDetails` body emits `{ status: 'invalid', errors: body.errors }`.
- U7 400 with a null body, a string body, or an object without `errors` emits `{ status: 'invalid', errors: {} }`.
- U8 415, 413, 500 and a network error (status 0) each error with an `HttpErrorResponse` of that status.
- U9 Type check: there is no `any`, and the `CreatePersonRequest`, `CreatePersonResult` and `CreatePersonOutcome` exports match `docs/contracts/006/types.ts`. `npm run build` passes.

## Mutation targets (each must make at least one test fail)

| Mutation | Caught by |
| --- | --- |
| Remove `InputRules.Normalize` in the handler | H2, H3, P2 |
| Remove the raw-length guard | H8 (801 spaces + Ada) |
| Drop `EF.Functions.Collate(..., "NOCASE")` in the pre-check | H4 (no-INSERT assertion) |
| Delete the pre-check | H4 |
| `catch (DbUpdateException)` without the `IsUniqueViolation` filter | H7 |
| Remove the unique-violation catch | H6, P11 |
| Return 200 / `Ok(result)` instead of `Created` | P1 |
| Remove `Uri.EscapeDataString` | P3 (ASCII assertion), K1 |
| `Location` built from the raw input | P2 |
| Remove `[JsonIgnore]` on `Name` | P1 (keys), P12 |
| Remove `[Consumes]` | P7a, P7b |
| Remove `[RequestSizeLimit]` | K3 |
| Re-add a try/catch returning `ex.Message` | K2 |
| Insert an `AstronautDetail` | H9, P14 |
| Log the name | H10 |
| Do not pass the `CancellationToken` | H11 |
| UI: trim the name before sending | U1 |
| UI: map 409 to `invalid`, or swallow 500 | U5, U8 |

## Regression

- All existing API tests stay green, including `PersonUpdateHttpTests`, `UpdatePersonHandlerTests` (after E1), `MigrationTests` and `PersonNameIndexMigrationTests`.
- `dotnet build -warnaserror` passes.
- `dotnet ef migrations has-pending-model-changes` reports no changes.
- All existing UI tests stay green.
