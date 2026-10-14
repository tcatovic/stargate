---
id: 001
title: Filter people by name, rank and current duty title
status: in-progress
platforms: [api, ui]
---

## Problem
API callers and the UI need to find people by name, current rank and current duty title, and to list everyone. Today:
- `GET /Person` returns all people (exists, no filtering, no paging).
- `GET /Person/{name}` returns one person by exact name (exists).
- There is no way to filter by rank or current duty title.
- `GET /Person/{name}` for an unknown name returns HTTP 200 with `person: null` instead of 404 (the response code defaults to success). CLAUDE.md requires 404 for an unknown person.
- `GetPersonByNameHandler` builds its SQL by string interpolation of the route value (`WHERE '{request.Name}' = a.Name`), which is a SQL injection hole. CLAUDE.md forbids interpolated SQL.
- Controller actions wrap everything in try/catch and return `ex.Message` as a 500 body, which leaks internals. Out of scope to fix globally, but the actions this task touches must not add to it.

## Outcome
A caller can retrieve all people, narrow the list by name, current rank and current duty title, and fetch a single person by name, all through RESTful, documented endpoints. Rank and duty title refer to the person's current values (AstronautDetail). People with no astronaut assignment (rule 2) still appear in the unfiltered list and are never matched by a rank or duty title filter.

## Decisions (agreed with the user)
- Query string on the collection: `GET /Person?name=&rank=&dutyTitle=`. Filters combine with AND; omitted filters do not filter.
- `name` matches case-insensitively as **contains**. `rank` and `dutyTitle` match case-insensitively as **exact**.
- `GET /Person/{name}` stays as the single-person lookup, exact and case-insensitive (consistent with name uniqueness in task 002).
- Rank and duty title accept any bounded string; no allowed-values list yet.
- No paging or sorting; the response shape of `GET /Person` is unchanged.
- Unknown name on `GET /Person/{name}` changes from 200 with `person: null` to **404**. This is a breaking change; the UI is updated in this task.
- Person responses keep the current shape (no duties included).
- `POST /Person` is not changed here.
- No authentication in this task (tracked with the API-key work).
- **Plan review answers:** (1) keep the input rule, so the literal string `x' OR '1'='1` returns 400 over HTTP (the `=` fails the character rule before any SQL runs); handler-level tests still show 404 / empty list for it, and apostrophe strings that pass the rule (`O'Brien`, `x' OR 'a' LIKE 'a`) prove the SQL is safe over HTTP. (2) **`Microsoft.AspNetCore.Mvc.Testing` is approved** for the test project only (test-only; version matching the .NET 10 runtime, 10.0.x), so HTTP-level tests cover the 400/404 responses, URL decoding and Swagger. (3) **ASCII-only case-insensitivity is accepted** (`élena` does not match `Élena`), documented and pinned by a test.

## Acceptance criteria
- [ ] `GET /Person` with no query string returns every person with the existing response shape (`people[]` of `personId, name, currentRank, currentDutyTitle, careerStartDate, careerEndDate`).
- [ ] `GET /Person` accepts optional `name`, `rank` and `dutyTitle` query parameters with the matching rules in Decisions. Filters combine with AND.
- [ ] A filter that matches nobody returns 200 with an empty `people` list.
- [ ] `GET /Person/{name}` returns 200 with the person when found and 404 when no such person exists; `responseCode` equals the HTTP status.
- [ ] People without AstronautDetail appear in the unfiltered list and are excluded by any `rank` or `dutyTitle` filter (rule 2).
- [ ] Filter values are validated and bounded (max length, allowed characters); invalid input returns 400.
- [ ] No filter or route value is concatenated into SQL. A test sends a name such as `x' OR '1'='1` to `GET /Person/{name}` and gets 404, and sends it as a `name` filter and gets an empty list.
- [ ] Names containing spaces or reserved URL characters round-trip correctly through `GET /Person/{name}`.
- [ ] Both endpoints are documented for Swagger, including the query parameters and the 400/404 responses.
- [ ] UI: `StargateApi` gains a method to list people with the optional filters, and `getPerson` handles the new 404; `models.ts` stays in step with the response; Vitest tests with `HttpTestingController` cover the query string, the empty result and the 404.
- [ ] Tests cover each filter alone, combined filters, case-insensitivity, no match, unassigned person, unknown name (404), and the injection cases.

## Out of scope
- Updating a person (task 002).
- Duty rules, `AstronautDuty` endpoints, and the global exception handler / try/catch removal.
- Authentication, paging and sorting.
- A new UI search screen. Only the API client method is in scope.

## Notes
Depends on: 005

Why: 005 makes the build warning-free and turns on `TreatWarningsAsErrors` (and adds the 404 fix for unknown names in the duties/add-duty paths) in files this task also changes. Starting from that tree avoids duplicate fixes and merge conflicts.

Domain rules involved: 1 (Name unique), 2 (no assignment means no astronaut rows).
The database currently has no tables on a fresh clone because the committed migration does not match the entities. Tests should use an in-memory or per-test SQLite database created from the model.
