# Task 008: data and server design contract (revision 1, amendment 1)

> **Amendment 1 (after merge; re-approval needed).** No design change. Two clarifications:
> 1. Section 2, validation step 1: a value made only of NON-ASCII whitespace is bound to null and means "no filter".
>    This is known and deliberately accepted, though the `InputRules` summary comment says non-ASCII whitespace is
>    always invalid.
> 2. Section 3, routing: on BASE a HEAD on `/Person/{x}` was already 405 (`Allow: GET, PUT`). `PUT /Person/abc`
>    returns 400 only with a JSON body; with no body it is 415 (`[Consumes]`), on both builds.

Markdown because agents read it as text. Baseline: `main` e950203.

## 1. Schema and migrations

None. No migration, no index, no constraint, no data change. `IX_Person_Name` (unique, NOCASE, task 006) is relied
on unchanged. Rollback is a code revert only.

## 2. The query object (api)

One class, `StargateAPI.Business.Queries.GetPeople`, is both the MVC `[FromQuery]` model and the MediatR request.
No wrapper, no second DTO.

| Property | C# type | Wire name (query key) | Attributes (all required, in any order) | Description text (verbatim) |
|---|---|---|---|---|
| `Name` | `string?`, `{ get; init; }` | `name` | `[FromQuery(Name = "name")]`, `[ValidName]`, `[Description(...)]` | `Case-insensitive exact match on the person's full name. Empty or whitespace-only means no filter.` |
| `Rank` | `string?`, `{ get; init; }` | `rank` | `[FromQuery(Name = "rank")]`, `[ValidName]`, `[Description(...)]` | `Case-insensitive exact match on the current rank (e.g. MAJ). Excludes people with no assignment.` |
| `DutyTitle` | `string?`, `{ get; init; }` | `dutyTitle` | `[FromQuery(Name = "dutyTitle")]`, `[ValidName]`, `[Description(...)]` | `Case-insensitive exact match on the current duty title (e.g. Commander, RETIRED). Excludes people with no assignment.` |

- No other public members. No `required` modifier, no `[Required]`, no `[StringLength]`, no `[RegularExpression]`
  (those validate the raw value; the point of this task is to validate the trimmed value like the writes).
- Action signature (exact): `public async Task<IActionResult> GetPeople([FromQuery(Name = "")] GetPeople query, CancellationToken cancellationToken)`;
  it sends `query` itself (`_mediator.Send(query, cancellationToken)`), never a copy. `Name = ""` pins the model
  prefix to empty: without it, a request containing a key equal to the parameter name (`?query=x`) switches MVC to
  the `query.` prefix and silently drops every filter and its validation (observed in the planning probe:
  `?query=x&name=%25` returned 200 with everyone). Keep a one-line comment saying so.
- Existing attributes on the action (`HttpGet("")`, `EndpointSummary`, `ProducesResponseType` 200/400/500) stay.
  `EndpointDescription` wording changes to the text in `openapi.yaml` (first paragraph only; the "Binding" and
  "Server implementation" paragraphs are for developers, not for the attribute).
- Handler `GetPeopleHandler` and its SQL are unchanged, including its private `Normalize` (null/whitespace -> null,
  else `InputRules.Normalize`). It stays as defence in depth for direct MediatR callers.

### Validation semantics (what the client observes)

For each of `name`, `rank`, `dutyTitle` independently:
1. Absent, empty, or `string.IsNullOrWhiteSpace` -> bound as `null` by MVC -> no filter, never a 400.
   **Known, deliberately accepted (Amendment 1):** `IsNullOrWhiteSpace` uses Unicode whitespace, so a value made
   ONLY of non-ASCII whitespace (`?name=%C2%A0`, `?rank=%E3%80%80`) is also null, meaning no filter (200, everyone;
   test Q6k). The `InputRules` summary (`InputRules.cs:10`) says non-ASCII whitespace is always invalid. That holds
   only when the value has other characters too (`?name=%C2%A0Elena%20Vasquez` is 400, Q6j). The result is the
   same public list as `GET /Person` and the same as BASE, so it is documented, not changed. A future tidy-up may
   reword that comment; it is not in this task's scope.
2. Otherwise `InputRules.Normalize` (trim tab, LF, VT, FF, CR, space at both ends) then `InputRules.IsValidName`
   (length 1..100 and `AllowedCharactersPattern`). Fail -> 400, `errors[<wire name>] = [NameRule message]`.
3. Valid -> the handler trims again (idempotent) and matches `= @value COLLATE NOCASE`.

## 3. Removal

- Delete `api/Business/Queries/GetPersonByName.cs` (`GetPersonByName`, `GetPersonByNameHandler`, `GetPersonByNameResult`).
- Delete the `GetPersonByName` action from `PersonController`.
- MediatR uses assembly scanning; there is nothing to unregister.
- Reword the comments at `Business/Commands/CreateAstronautDuty.cs:77` and `Business/Queries/GetAstronautDutiesByName.cs:18`
  so they describe the resolution order (exact-case match first, then lowest Id) without naming the deleted type.
- Postcondition: `grep -rnw GetPersonByName exercise1/api exercise1/api.Tests exercise1/ui/src` (excluding `bin/`,
  `obj/`, `node_modules/`) prints nothing. `GetPersonByNameResult` likewise. `docs/` history is not edited.
- Routing consequence (normative): `GET|HEAD /Person/{x}` -> 405, `Allow: PUT`, empty body, because
  `PUT /Person/{personId}` (no route constraint) matches the path. Do NOT add a route constraint to PUT and do NOT
  add a catch-all GET. PUT behaviour must not change: `PUT /Person/abc` with a JSON body -> 400 (binding error);
  with no body / no Content-Type -> 415 (`[Consumes("application/json")]`), on BASE and HEAD alike.
- HEAD (Amendment 1): on BASE, `HEAD /Person/{x}` was already 405 with `Allow: GET, PUT`, because `[HttpGet]` does
  not match HEAD and there was no `[HttpHead]`. Only the `Allow` value changes, to `PUT`.

## 4. Swagger parameter filter

New file `api/Swagger/ValidNameParameterFilter.cs`, `namespace StargateAPI.Swagger`, `public sealed class
ValidNameParameterFilter : IParameterFilter`. `Apply(IOpenApiParameter parameter, ParameterFilterContext context)`:
if `context.PropertyInfo` (else `context.ParameterInfo`) has `ValidNameAttribute` and `parameter.Schema is
OpenApiSchema schema`, set `schema.MaxLength = InputRules.MaxLength` and `schema.Pattern =
InputRules.AllowedCharactersPattern`. Nothing else. Registered in `Program.cs` as
`builder.Services.AddSwaggerGen(options => options.ParameterFilter<ValidNameParameterFilter>());`.
(Verified to compile and to emit the expected output under Swashbuckle 10.3.0 / Microsoft.OpenApi 2.x in the
planning probe.)

## 5. Domain rules

| Rule | How it stays true |
|---|---|
| 1 Person identified by name | The single by-name lookup is `GET /Person?name=`: trimmed, exact, ASCII case-insensitive, parameterised SQL, unique NOCASE index. At most one person is returned for a name filter on a migrated DB. Unknown -> 200 empty list. (On a database that predates the unique index and still holds case variants, the list returns every variant: `PersonResolutionPreIndexHttpTests` asserts ids 4 and 9 for `bob`/`Bob`/`BOB`.) |
| 2 Unassigned people have no astronaut rows | SQL unchanged: `LEFT JOIN AstronautDetail`; `rank`/`dutyTitle` compare the joined columns, so an unassigned person never matches them; with only `name` they are returned with `currentRank`/`currentDutyTitle` = `""` and null dates. |
| 3-7 | Not touched (task 009). |

## 6. Security notes

- Values only ever travel as Dapper parameters (unchanged). `%` and `_` fail NameRule (400) before SQL; the match is
  `=`, so no wildcard semantics exist anyway.
- The 400 body never echoes the value (ValidName's message is static; binding messages already non-echoing,
  `Program.cs:8-17`).
- The prefix pin closes a validation bypass that a naive complex-type binding would open (section 2). Locked by
  Q5a-Q5k (tests.md), including `query=`, `query[0]`, `query.foo`, `QUERY`, `query.rank`, `query.name`, indexed
  `name[0]` and odd encodings.
- Input size: the query string is bounded by Kestrel's request-line limits (unchanged); `IsValidName` checks length
  before the regex (100 ms timeout), so a very long value is cheap to reject.
