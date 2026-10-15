---
id: 008
title: Replace GetPersonByName with one GetPeople query object and exact name match
status: in-progress   # draft | ready | planned | approved | in-progress | done | blocked
platforms: [api, ui]
---

## Problem
After task 001 the API has two overlapping ways to find a person: `GET /Person?name=...` (the `GetPeople` query, which also accepts `rank` and `dutyTitle`) and `GET /Person/{name}` (the `GetPersonByName` query and handler). The spec requirement "Retrieve a person by name" is already met by the filtered collection, so the second route, handler, result type and UI method are duplicated surface that must be kept in step (validation, matching rule, response shape, tests).

Three further problems in the task 001 design:
- The filter inputs reach the controller and the `GetPeople` request as separate loose parameters (`name`, `rank`, `dutyTitle`). Validation rules, Swagger documentation and the UI's `PersonFilters` each restate the same three fields.
- `name` is implemented as a contains match using `LIKE ... ESCAPE` plus wildcard escaping in the query code. Rule 1 says a person is identified by name, so a lookup by name should be exact. The escaping code exists only to support partial matching nobody asked for.
- At the time this draft was written, `AstronautDutyController` sent `GetPersonByName` instead of the duties query (the bug task 007 fixes). Task 007 rewires it, so by the time this task runs the controller no longer references `GetPersonByName`; this task only deletes the type and verifies nothing else uses it.

Note on references: the task 001 code lives on the open PR #4 branch (`task/001-search-people-by-name-rank-duty-title`). The author of this draft could not read that branch (no git access in the drafting session), so this Problem section names files and symbols but gives no line numbers. The architect must confirm the exact locations against `main` once PR #4 is merged. Files expected to be involved: `exercise1/api/Business/Queries/GetPeople.cs`, `exercise1/api/Business/Queries/GetPersonByName.cs`, `exercise1/api/Business/Validation/InputRules.cs`, `exercise1/api/Controllers/PersonController.cs`, `exercise1/api/Controllers/AstronautDutyController.cs`, `exercise1/ui/src/app/api/stargate-api.ts`, `exercise1/ui/src/app/api/models.ts`, and `docs/contracts/001/`.

## Outcome
"Retrieve a person by name" is `GET /Person?name=<name>` and nothing else.
- `GetPersonByName` (request, handler, result type) is deleted, together with `GET /Person/{name}` and the UI's `getPerson` and its result type.
- The filter inputs are ONE query object: a single class bound with `[FromQuery]` on `PersonController`, with the validation attributes on its properties, used as (or thinly wrapped into) the MediatR `GetPeople` request. The UI's `PersonFilters` mirrors it field for field.
- `name` is an exact match: case-insensitive and trimmed, consistent with name uniqueness (rule 1). `rank` and `dutyTitle` stay case-insensitive exact matches. The `LIKE ... ESCAPE` clause and the wildcard-escaping code are removed.
- An unknown name returns 200 with an empty `people` list (a filtered collection), not 404.

## Decisions (agreed with the user)
- **Revised after task 006's plan review:** the **exact, case-insensitive, trimmed `name` filter and the deletion of the `LIKE ... ESCAPE` machinery are done in task 006**, together with the ASCII-only name rule. What remains for this task: remove `GET /Person/{name}` with `GetPersonByName` (request, handler, result) and its tests, add a routing-404 test for the old route, remove the UI's `getPerson` and `GetPersonByNameResult`, introduce the single `[FromQuery]` query object (also the `GetPeople` request) with `PersonFilters` mirroring it, and unify GET trimming with the write endpoints. Task 006's `Location` header already points at `/Person?name=`. Wherever the criteria below mention making `name` exact, removing the LIKE code or the `?name=ra` test, treat it as already done and keep only a regression check.
- Remove `GetPersonByName` and `GET /Person/{name}`. `GET /Person?name=<name>` replaces them.
- One query object for the filters, bound with `[FromQuery]`; the same class (or a thin wrapper) is the MediatR request. Whether it is named `GetPeople` itself or a separate `PersonQuery` is left to the architect, subject to the open question below.
- `name` is exact, case-insensitive, trimmed. Not contains, not starts-with.
- No `exact=true` option. The user declined it.
- Unknown name is 200 with an empty list. The 404 that task 001 introduced for `GET /Person/{name}` disappears with that endpoint. This applies to the `people` collection only; the duties endpoint (task 007) answers for one person's view and may return 404 for an unknown name.
- If richer querying is wanted later (partial match, paging, sorting), the user will implement OData. None of that is built here.
- Everything else stays as in task 001: `InputRules`, the 100-character bound, the allowed-character rule, the 400 body shape (no input echo), people without an astronaut assignment never match `rank` or `dutyTitle` (rule 2), the response shape of `GET /Person`, and ASCII-only case-insensitivity.
- **Removed route:** a request to `GET /Person/{name}` after this task gets a plain routing **404**, with no deprecation period (pre-release API, single consumer, and the UI changes in the same commit).
- **One class:** the query object is a single class used as the MediatR `GetPeople` request (one definition); split it only if the architect shows the validation attributes cannot sit there.
- **Empty result:** unchanged shape: 200, `success: true`, empty `people`, no special message.
- **Validation:** a `name` that fails the character rule (for example containing `%`) still returns **400**, as in task 001.
- Breaking changes (to be called out in the changelog): `GET /Person/{name}` is removed; `GET /Person?name=` changes from contains to exact; an unknown person via the by-name lookup changes from 404 to 200 with an empty list.

## Plan review answers (accepted by the user)
- **The removed `GET /Person/{name}` route returns 405** (Allow: PUT, empty body), because the path still matches the route template of `PUT /Person/{personId}`; this replaces the earlier "plain 404" assumption. No `{personId:int}` constraint is added in this task (that would change `PUT /Person/abc` from 400 to 404).
- **The over-long-value 400 carries the single shared name-rule message**, the same as the write endpoints (trimming is unified; the raw value is no longer validated).
- **Binding:** the query object is `GetPeople` itself, bound with `[FromQuery(Name = "")]` on the action parameter so a caller cannot skip validation (`?query=x&name=%25`), drop the filter, or alias `query.name`; tests Q5a-Q5f keep it fixed. Wire names stay `name`, `rank`, `dutyTitle`.
- `CLAUDE.md`'s layout line is updated in this task's PR to `(GetPeople, GetAstronautDutiesByName)`.

## Acceptance criteria
API, removed route
- [ ] `GET /Person/{name}` no longer exists. A request to it returns 404 from routing (no handler runs, no person data in the body). There is no redirect and no 410. See the open question on deprecation.
- [ ] `GetPersonByName`, its handler, `GetPersonByNameResult` and any DI or registration for them are gone from the API project; the solution builds with `-warnaserror`.
- [ ] `AstronautDutyController` has no reference to `GetPersonByName` (this is satisfied by task 007; this task verifies it and fails the build if the type is still referenced).

API, query object
- [ ] `PersonController.GetPeople` takes one `[FromQuery]` object instead of separate `name`, `rank` and `dutyTitle` parameters. The same class (or a thin wrapper) is what is sent to MediatR.
- [ ] The object's properties carry the validation attributes (length bound of 100 after trimming, allowed-character pattern). The 400 response for an invalid value has the same shape as in task 001 and does not echo the input.
- [ ] Binding is case-insensitive on parameter names as before (`?name=`, `?Name=`); unknown query parameters are ignored, not an error. Omitting all parameters returns every person, as in task 001.
- [ ] Swagger shows the object's properties as individual query parameters with their `maxLength` and `pattern`, plus the 200 and 400 responses.

API, matching
- [ ] `?name=Elena%20Vasquez` returns Elena Vasquez; `?name=elena%20vasquez` and `?name=%20%20ELENA%20VASQUEZ%20` also return her (the full name is required: exact match) (case-insensitive, trimmed).
- [ ] `?name=ra` (a fragment of an existing name) returns 200 with an empty `people` list. Substring, prefix and suffix fragments never match.
- [ ] A name that does not exist returns 200 with an empty `people` list and `success: true`, and `responseCode` equal to 200.
- [ ] `rank` and `dutyTitle` remain case-insensitive exact matches.
- [ ] Combined filters are ANDed: `?name=X&rank=Y` returns only a person who matches both.
- [ ] A person with no astronaut assignment is never returned when `rank` or `dutyTitle` is supplied, and is returned when only `name` matches (rule 2).
- [ ] Values containing quotes, `%`, `_`, `;`, `--` or SQL fragments are treated as data: no 500, no leak, no wider result set. A `%` or `_` in `name` does not act as a wildcard (it is either rejected by the character rule with 400 or matches nothing; it must match no more than the literal text).
- [ ] The `LIKE`, `ESCAPE` and wildcard-escaping code is removed from `GetPeople` and any helper it used; a code search finds no remaining use.
- [ ] The response shape of `GET /Person` (`people` items, `success`, `message`, `responseCode`) is unchanged.

UI
- [ ] `StargateApi.getPerson` and `GetPersonByNameResult` are removed from `stargate-api.ts` and `models.ts`; nothing imports them.
- [ ] `PersonFilters` in `models.ts` mirrors the API query object's fields exactly. `listPeople` behaves as before (same params sent via `HttpParams`, same loading, empty and error states).
- [ ] Vitest tests with `HttpTestingController` cover `listPeople` with and without `name`, and no test references `getPerson`.

Tests and docs
- [ ] xUnit tests that targeted `GetPersonByName` or `GET /Person/{name}` (handler tests, `WebApplicationFactory` HTTP tests, null-guard and response-shape tests) are removed or rewritten against `GET /Person?name=`. A test asserts that `GET /Person/{name}` returns 404.
- [ ] Tests for the contains behaviour and for wildcard escaping from task 001 are replaced by exact-match tests (including the `?name=ra` case above).
- [ ] The changelog in `exercise1/api/README.md` lists the breaking changes under Decisions.
- [ ] Any contract or doc still describing `GET /Person/{name}` or contains matching is updated or marked superseded (the 001 contract under `docs/contracts/001/` stays as history; this task does not need to edit it).

## Out of scope
- OData, or any richer querying: partial, contains or starts-with matching, paging, sorting.
- An `exact=true` option or any alternative lookup mode.
- Changes to `GET /AstronautDuty` or its route, handler or shape (task 007). This task only deletes `GetPersonByName` once 007 has stopped using it.
- Unicode normalisation (NFC) or invisible-letter rejection in `InputRules` (task 002). See Notes.
- Changes to `POST /Person` (task 006) or rename (task 002).
- Changing `InputRules`, the 100-character bound, or the 400 body shape, unless a change is trivially required by the query-object refactor, in which case the architect calls it out for the user.
- Authentication, CORS, new endpoints.

## Notes
Depends on: 001, 007

Why: 001 is the baseline (the `GetPeople` filters, `InputRules`, and the UI `PersonFilters` that this task consolidates). 007 rewires `AstronautDutyController` so it no longer sends `GetPersonByName`; until that is merged on `main`, deleting `GetPersonByName` breaks the duties endpoint's build. So this task must be planned and built after 007 is `done`, not before.

Interplay with the task 002 follow-up (NFC and invisible letters in `InputRules`): with exact matching, a name typed in a different Unicode encoding than the stored one (for example NFD `e` plus a combining mark versus NFC `é`) will return an empty list instead of a match, where a contains match might sometimes have found it. That is accepted for now. Task 002 will NFC-normalise names before they are stored and compared, and the same normalisation should then be applied to the `name` filter. This task does not change that rule.

An empty result on a lookup that used to be 404 may hide typos from callers. Callers that need to know whether a person exists must now check for an empty `people` list. This is intentional (filtered collection semantics) and part of the breaking change.

A request to the removed route returns the framework's 404, which is also what an unknown person used to return on that route. Old clients that only look at the status cannot tell the two apart. See the open question.

Domain rules involved: 1 (name identifies a person, so exact lookup), 2 (people without an assignment have no astronaut rows, so they never match `rank` or `dutyTitle`).
