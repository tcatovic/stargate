---
id: 007
title: Fix the AstronautDuty endpoints (SQL injection, duties by name query param, validated create)
status: done   # draft | ready | planned | approved | in-progress | done | blocked
platforms: [api, ui]   # api | ui | infra | db
---

## Problem
The user decided that the AstronautDuty endpoints are addressed in one pull request ("we are going to address the AstronautDuty in another pr"). This task is that PR. Everything below was confirmed by reading the code on branch `task/006-create-person-endpoint`. No QA report for task 006 exists in `docs/qa/` yet, so nothing here relies on one.

**1. Second-order SQL injection (security, highest priority).** Both AstronautDuty handlers build SQL by string interpolation of the person's name:
- `CreateAstronautDuty.cs` line 56: `SELECT * FROM [Person] WHERE '{request.Name}' = Name`. Lines 70 and 100 also interpolate `{person.Id}` (an int, so not exploitable, but still interpolated SQL, which CLAUDE.md forbids).
- `GetAstronautDutiesByName.cs` line 28: `... WHERE '{request.Name}' = a.Name`, and line 39 interpolates `{person.PersonId}`.

`CreateAstronautDutyPreProcessor` only checks, with a parameterised EF query, that the exact string exists as a stored person name. The handler then looks the person up again by interpolation. Person names may legally contain apostrophes and spaces (letters, digits, space, hyphen, apostrophe, period, at least one letter or digit), and `POST /Person` has no authentication. So a person can be created with the name `a' OR Id IS 3 OR 'a`, which passes the name rule. `POST /AstronautDuty` with that name passes the pre-processor (the string exists) and the handler then runs `WHERE 'a' OR Id IS 3 OR 'a' = Name`, which returns the person with Id 3. The new duty, the AstronautDetail update and the "close the previous duty" update are applied to ANOTHER person. The same payload on the GET handler reads another person's duties. The attacker needs no credentials.

Even without an attacker, any legitimate name with an apostrophe (for example `Aoife O'Brien Jr.`) makes `POST /AstronautDuty` fail with an unhandled syntax error and a 500.

**2. `GET /AstronautDuty/{name}` returns no duties.** `AstronautDutyController.GetAstronautDutiesByName` sends `GetPersonByName` instead of `GetAstronautDutiesByName`, so the response has only `person` and no `astronautDuties`, although the UI model and `StargateApi.getDuties` expect them. `GetAstronautDutiesByNameHandler` is unreachable today. It carries the null-person guard from task 005 (returns a null `Person` with 200) but no real 404. The action wraps the call in `try/catch` and returns `ex.Message` as the 500 body, which leaks internals. The route also does not follow the query-param convention used for collection reads (tasks 001 and 008).

**3. `POST /AstronautDuty` is not fit for use.** From the code:
- Success returns the default `BaseResponse` shape: HTTP 200 with `{ id, success, message, responseCode }`, no `Location`.
- Unknown person: the pre-processor throws `BadHttpRequestException("Bad Request")`. Task 005 records this as 400 (not re-run for this draft); the action has no try/catch, so in Development the response is a developer exception page with a stack trace. The handler's own 404 branch ("Person not found", from task 005) is unreachable.
- Duplicate: the same pre-processor throws the same exception, so a duplicate is also a 400 with a stack-trace page. The check (`DutyTitle == x && DutyStartDate == y`, line 37) is NOT scoped to the person, so person B cannot start the same title on the same day as person A. It also compares the raw request date, including any time part, against date-only stored values, so a duplicate sent with a time component slips through.
- No validation: `Name` is only `required`. `Rank` and `DutyTitle` are unbounded free text. `DutyStartDate` may be omitted and silently becomes `0001-01-01`. No `[Consumes]` and no `[RequestSizeLimit]`, unlike the Person endpoints.

## Outcome
Names (and every other input) can never alter the SQL that runs. A caller can fetch a person's duties with `GET /AstronautDuty?name=<name>` and add a duty with a validated `POST /AstronautDuty` that behaves like the Person endpoints: shared name rule, consistent 400/404/409 bodies, no exception text, no stack trace. The UI API client matches.

Which person an operation applies to is decided exactly once per request and the same identity is used for every later read and write. (The architect decides how. Recommended: drop `CreateAstronautDutyPreProcessor`, as task 006 dropped `CreatePersonPreProcessor`, and have the handler do one parameterised, case-insensitive lookup by the normalised name, then use that person's id for all later queries. Reason: the pre-processor cannot hand the person it loaded to the handler, so keeping both means two lookups that can disagree, which is the root of the injection.)

## Acceptance criteria
Security (first, and the reason this task exists)
- [ ] No SQL in `CreateAstronautDuty.cs` or `GetAstronautDutiesByName.cs` (nor any code they call) is built by string interpolation or concatenation; values travel only as parameters or through EF LINQ. This includes the `person.Id` / `person.PersonId` queries. A code search for `$"SELECT`, `$"""` and `FromSqlRaw` in these files finds nothing.
- [ ] Hostile name end to end, write path: a person named `a' OR Id IS 3 OR 'a` is created through `POST /Person` (201). `POST /AstronautDuty` with that name then adds the duty to that person only. All Person, AstronautDetail and AstronautDuty rows of every other person are byte-for-byte unchanged (compared before and after). A second payload using a digit (`a' OR 1 OR 'a`) behaves the same.
- [ ] Hostile name end to end, read path: `GET /AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a` returns that person's own duties (or an empty list), never another person's. A name that does not exist but looks like a payload returns the unknown-person result, not a wider result set.
- [ ] Legitimate apostrophe works: after `POST /Person` for `Aoife O'Brien Jr.`, `POST /AstronautDuty` for that name succeeds, and `GET /AstronautDuty?name=Aoife%20O%27Brien%20Jr.` returns the new duty. No 500.
- [ ] A payload that fails the name rule (for example `x' OR '1'='1`, which contains `=`) sent to `POST /AstronautDuty` or `GET /AstronautDuty` returns 400 before any SQL runs, and the 400 body does not echo it.
- [ ] The hostile-name tests are written so they fail against the current code (the injection test must fail on the unfixed implementation), and they go through the real HTTP stack with `WebApplicationFactory` and SQLite, not only the handlers.
- [ ] `rank` and `dutyTitle` in the POST body are also treated as data (a body containing quotes or SQL fragments that passes the rule is stored verbatim; one that fails the rule is 400).

GET /AstronautDuty
- [ ] `GET /AstronautDuty?name=<name>` sends `GetAstronautDutiesByName` (not `GetPersonByName`) and returns 200 with `person` and `astronautDuties` ordered by `DutyStartDate` descending.
- [ ] Lookup is exact, trimmed and case-insensitive (ASCII), consistent with `GET /Person?name=` and rule 1: `name=elena%20vasquez` and `name=%20ELENA%20VASQUEZ%20` find Elena Vasquez; a fragment such as `ra` does not match.
- [ ] A person with no astronaut assignment returns 200 with `person` set and an empty `astronautDuties` (rule 2).
- [ ] Unknown name: 404 with the fixed message "Person not found" (no echo of the input), `person` null, `astronautDuties` empty. (Open question 1: 404 vs 200 with an empty result. This criterion changes with the answer.)
- [ ] `name` is required: missing, empty or whitespace-only returns 400. (Open question 2.)
- [ ] `name` is validated with the shared ASCII name rule (`InputRules` / `[ValidName]`, tasks 002 and 006), so behaviour matches `GET /Person?name=`: over 100 characters, a disallowed character, or no letter or digit returns 400 as `ValidationProblemDetails` with no input echo.
- [ ] `GET /AstronautDuty/{name}` no longer exists: a request returns the framework's routing 404 with no person data. (Open question 3.)
- [ ] `AstronautDutyController` has no reference to `GetPersonByName` and no `try/catch`; no response contains an exception message, stack trace or SQL text. An unexpected failure is a generic 500 (empty body in Production, developer page in Development, as in tasks 001/002/006); the global exception handler is a separate follow-up.
- [ ] `BaseResponse.ResponseCode` equals the HTTP status on every response.

POST /AstronautDuty
- [ ] Validation matches the Person write endpoints: `name` uses the shared name rule (`[Required, ValidName]`); `rank` and `dutyTitle` are required and bounded by the same shared length and character rule (a title such as `Mission Specialist` or `RETIRED` stays valid); `dutyStartDate` is required (omitting it is 400, not `0001-01-01`). Invalid input returns 400 as `ValidationProblemDetails`, writes nothing and echoes nothing. Missing or malformed JSON is 400 without parser internals (same hardening as the Person endpoints).
- [ ] The action has `[Consumes("application/json")]` and `[RequestSizeLimit(4096)]` like `POST /Person` (non-JSON is 415).
- [ ] Unknown person: 404 with "Person not found" and nothing written. It is no longer a `BadHttpRequestException`, and no stack trace is ever returned. (Open question 4.)
- [ ] Duplicate duty (same person, same title, same start date; the comparison uses the date part only): 409 with a fixed message and nothing written. The same title and date for a DIFFERENT person is allowed. (Open question 4.)
- [ ] Success: 201 Created with the new duty's `id` and `responseCode` 201 in the body and a `Location` header pointing at `/AstronautDuty?name=<URL-encoded stored name>`. (Open question 5.)
- [ ] The persisted result of a successful create is unchanged from today for the same inputs (new duty row, AstronautDetail created or updated, previous latest duty closed at start minus 1 day). A regression test pins the current behaviour of rules 3-7 for a normal "second duty arrives" case so the refactor away from raw SQL cannot change it. It does not endorse the known defects listed under Out of scope.
- [ ] Endpoint documented for Swagger: summary, description, request schema, and the 201/400/404/409/415 responses.

UI
- [ ] `StargateApi.getDuties(name)` sends `name` through `HttpParams` as `GET /api/AstronautDuty?name=...` (no path segment, special characters such as `'` and spaces correctly encoded) and handles the unknown-person result according to the answer to question 1 without turning other failures into "not found".
- [ ] `models.ts` stays in step with the response (`GetAstronautDutiesByNameResult`, `AstronautDuty`), and the 400 body is typed with the existing `ValidationProblemDetails`.
- [ ] Vitest tests with `HttpTestingController` cover: the query string (including an apostrophe and spaces), a result with duties, the empty-duties result, the unknown-person result, and an error (400/500) that propagates.

Tests and docs
- [ ] xUnit handler tests and `WebApplicationFactory` integration tests cover every case above, including the hostile-name and `Aoife O'Brien Jr.` tests. `NullPersonGuardTests` (which documents the handlers as unreachable) is rewritten for the new behaviour; its guards become real, reachable paths covered end to end.
- [ ] The changelog in `exercise1/api/README.md` lists the breaking changes: the removed `GET /AstronautDuty/{name}` route, the new `name` query parameter, and the changed POST status codes.

## Out of scope
- **The duty rules 3-7** (their behaviour is untouched here, and the defects below are known and stay): closing the previous duty at start minus 1 day for out-of-order or back-dated starts (today the latest-by-start duty is always closed); `RETIRED` handling, including the title comparison being case-sensitive; `CareerEndDate`: for a person whose FIRST duty is `RETIRED` the create path sets it to the start date instead of start minus 1 day (rule 7), and a later non-retired duty does not clear it; concurrency and transactions for duty writes (a database unique index and a race test); overlapping or same-start-date duties for one person. These are proposed as a separate follow-up task, "Enforce duty rules 3-7 in the create path". Only a one-line fix that cannot be separated from the injection fix may be taken here, and the architect must call it out.
- Pagination, and filters such as `rank` or `dutyTitle` on the duties GET.
- A global exception handler, authentication and CORS (tracked separately).
- A UI screen, and a `StargateApi.createDuty` method (open question 7).
- Any schema change or migration.
- `GET /Person` and the other Person endpoints (tasks 001, 002, 006, 008).

## Notes
Depends on: 005, 006

Why: 005 put the null-person guards and warning-free build into these exact handlers and made the 404 path in the add-duty handler. 006 finalises the shared name rule (ASCII, at least one letter or digit), `[ValidName]`, the `ValidationProblemDetails` 400 shape, the malformed-JSON message hardening and the `Location` convention, all of which this task reuses; it is not merged yet, so this task cannot be planned until it is `done`. Task 002 (rename, unique `Person.Name` index) is already a dependency of 006.

**Overlap with task 008 (it depends on this task).** 008 deletes `GET /Person/{name}`, `GetPersonByName` and the UI's `getPerson`. `GetPersonByName` cannot be deleted while `AstronautDutyController` references it, so this task removes that reference (a criterion above) and 008 only verifies it. 008's Out of scope already leaves `GET /AstronautDuty` to this task. This task does not touch `PersonController`, `GetPersonByName` or the UI's `getPerson`. Suggested wording change for 008 (not made here; I was told not to edit it): its Problem bullet and its breaking-changes bullet describe the duties endpoint's use of `GetPersonByName` and call the loss of the shared lookup "a side effect"; after 007 merges that is already done, so 008 can drop the "side effect" line from its changelog list and describe the controller dependency as already satisfied. Its decision "unknown name is 200 with an empty list" applies to the `people[]` collection only and does not conflict with a 404 here (see question 1), but 008's author may want one sentence saying so.

Superseded wording in the previous draft: "only trim and length apply" and "the full allowed-character name rule is out of scope". The shared ASCII rule now applies here so that `GET /AstronautDuty?name=` accepts and rejects exactly what `GET /Person?name=` does. The previous draft's "without `name`, return duties for all people" is replaced by question 2.

Observation: the duty items returned by the Dapper query are the `AstronautDuty` entity, which has a `Person` navigation property, so each item may serialise a `"person": null` member that the UI model does not declare. The architect should check the actual JSON and decide whether to return a dedicated item type. That would be a shape change to call out.

Domain rules involved: 1 (name identifies a person, so exact lookup), 2 (no assignment means an empty duty list), and 3-7 only as the behaviour that must not regress.

### Decisions (agreed with the user: "accept all" on the nine open questions)
1. **Unknown name on `GET /AstronautDuty?name=` returns 404.** This is one person's view (`person` plus `astronautDuties`); a typo must differ from "known person, no duties". Task 008's 200-with-empty-list applies only to the `people` collection.
2. **`name` is required** on the duties GET (400 when missing). No all-people dump in this task.
3. **`GET /AstronautDuty/{name}` is removed outright**, no deprecation period (breaking; pre-release API, one consumer changed in the same commit).
4. **`POST /AstronautDuty`: unknown person returns 404 and a duplicate returns 409**, with the duplicate check scoped to the same person.
5. **`POST /AstronautDuty` success is 201** with `Location: /AstronautDuty?name=<encoded>` and the `id` in the body, like `POST /Person` (breaking, 200 to 201).
6. **The POST body keeps the person's `name`** (no `personId`). The injection is fixed by parameterising / reusing the single resolved person.
7. **No UI `createDuty` client method** in this task.
8. **Scope:** this one PR includes the full POST hardening (validation, 404/409/201), not only the injection fix.
9. **The follow-up for duty rules 3-7 is written now** (task 009) and queues behind this one.

**Plan review answers (accepted by the user):**
- `dutyStartDate` is required and bounded to **1900-01-01 .. 2100-12-31** (an omitted date crashes today for a person who already has a duty, and silently becomes 0001-01-01 otherwise).
- **Rank and duty title are trimmed before storage**, so a padded `" RETIRED "` counts as retired (task 009 trims titles too).
- The duplicate check **ignores title case** (`Pilot` and `pilot` on the same day are duplicates), scoped to the same person and date.
- Design (from the approved plan): `CreateAstronautDutyPreProcessor` is deleted; the create handler resolves the person once with a parameterised, trimmed, case-insensitive EF query and uses only that id afterwards; GET uses fixed parameterised Dapper queries; duty items use a dedicated six-field DTO; no transaction and no new indexes in this task (task 009); no source-scan test; rules 3-7 behaviour is otherwise unchanged and pinned by a regression test.

The security fix remains the first and non-negotiable part of this task: it was deliberately deferred out of task 006 (see its PR) and must land before any public deployment.
