---
id: 006
title: Make POST /Person a proper, validated create endpoint
status: in-progress   # draft | ready | planned | approved | in-progress | done | blocked
platforms: [api, ui]   # api | ui | infra | db
---

## Problem
The user needs to be able to add another person. A `POST /Person` already exists, so adding a person technically works, but it is not fit for use. Confirmed by reading the source:

- **Bare-string body.** `PersonController.CreatePerson([FromBody] string name)` requires the JSON body to be a quoted string (`"Ada Lovelace"`). That is awkward to document, cannot grow new fields, and differs from the object bodies used elsewhere (`POST /AstronautDuty`, and `PUT /Person/{personId}` in task 002).
- **Duplicates are not rejected.** `CreatePersonPreProcessor` exists but is never registered: `Program.cs` only calls `AddRequestPreProcessor<CreateAstronautDutyPreProcessor>()`. The handler inserts whatever it is given, so two people with the same name can be created today (breaks rule 1). Even if it were registered, it compares with `==` (case-sensitive, untrimmed) and throws `BadHttpRequestException`, which the controller's `catch (Exception)` turns into a 500 carrying `ex.Message`.
- **No validation.** Empty, whitespace-only, null, very long or oddly-charactered names are all stored as given. No trimming.
- **Wrong success shape.** A successful create returns HTTP 200 with `{ id, success, message, responseCode }` and no `Location`; it should be 201 Created.
- **Error leakage.** The controller returns `ex.Message` in a 500 body (CLAUDE.md forbids leaking internals).
- **UI.** `StargateApi` (`exercise1/ui/src/app/api/stargate-api.ts`) only has `getPerson`; there is no `createPerson`.

What already works: the route exists, a person row is inserted, and its new `Id` is returned. A new person gets no `AstronautDetail`/`AstronautDuty` rows (rule 2) because the handler only creates the `Person`; this must stay true and be tested.

## Outcome
A caller can add a person with a documented JSON object body, gets a 201 Created response identifying the new person, and any invalid or duplicate name is rejected with a clear 400 or 409 and nothing is stored. The UI API client can call it.

## Decisions (agreed with the user)
- **This task owns all `POST /Person` behaviour**, including the duplicate 409. Task 002 keeps only the unique index and the rename.
- **Name rule: ASCII only (revised after plan review).** Names are trimmed, 1 to 100 characters, and may contain only ASCII letters, digits, space, hyphen, apostrophe and period. All Unicode handling (NFC normalisation, invisible-character rejection, combining marks, the 800-character raw bound) is removed from the merged 001/002 code in a separate commit set of this task; accented and other non-ASCII names now get 400 (deliberate). Only ASCII whitespace is trimmed; a non-ASCII whitespace character at an edge is rejected, not trimmed. The look-alike / NFKC question is moot.
- **`Location` header** points at `/Person?name=<Uri.EscapeDataString(stored name)>`. Task 008 removes `GET /Person/{name}`, so the header must not point at it. A `GET /Person/{id}` route remains a separate task.
- **`name` filter is exact now (revised after plan review):** `GET /Person?name=` becomes a case-insensitive, trimmed EXACT match in this task (no contains), and the `LIKE ... ESCAPE` and wildcard-escaping code from task 001 is deleted. This brings forward the exact-name part of task 008; 008 keeps only the removal of `GET /Person/{name}` and the single query object. `?name=ra` returns an empty list; the full name (for example `Elena Vasquez`, case-insensitive) finds her.
- **Existing non-ASCII rows:** no data migration and no database constraint. Names already stored with non-ASCII characters stay, can be renamed by id, but cannot be looked up by name.
- **500 body:** empty in Production and the developer page in Development, as in tasks 001/002. A global exception handler is a separate follow-up task.
- **Pre-processor:** `CreatePersonPreProcessor` is deleted, not registered; duplicate handling lives in the handler plus the unique-violation mapping.
- **Work order inside this task:** (C1) ASCII-only rule, (C2) exact filter, (C3) POST feature, (C4) UI `createPerson`; C1 and C2 are separate commits for review.
- **Break the request body cleanly**: object body `{ "name": "..." }` only; the bare-string body is no longer accepted. Nothing in the repo calls it (the UI has no `createPerson`). The user is aware this breaks any external caller.
- **201 response body returns just the `id`** (existing field name), alongside `success`, `message` and `responseCode`. No rename to `personId`.

## Acceptance criteria
- [ ] `POST /Person` accepts `{ "name": "..." }` and, on success, returns 201 Created with a body containing the new person's `id` and `responseCode` 201, plus a `Location` header pointing at `/Person?name=<encoded name>`.
- [ ] The name is trimmed before it is validated, compared or stored; the stored name has no leading or trailing whitespace.
- [ ] Invalid input returns 400 and stores nothing: missing body, `null`/missing `name`, empty or whitespace-only name, name longer than the maximum, and names containing disallowed characters (the shared name rule above). `responseCode` equals the HTTP status.
- [ ] A name equal to an existing person's name, ignoring case and surrounding whitespace, returns 409 and stores nothing (rule 1). Example: `ada` / `  ADA ` after `Ada` exists.
- [ ] The 409 also holds when the pre-check passes but the database unique index from task 002 rejects the insert (concurrent creates): exactly one succeeds, the other gets 409, never a 500 and never two rows. A test exercises the database constraint path.
- [ ] No response from this endpoint contains an exception message, stack trace or SQL text. Unexpected failures return a generic 500 message (and are logged).
- [ ] Creating a person adds no `AstronautDetail` or `AstronautDuty` rows (rule 2); a test asserts both tables are unchanged. The new person then appears in `GET /Person` with null rank, duty title and career dates.
- [ ] The dead/misregistered duplicate check is resolved: there is a single, registered mechanism for the duplicate check (no unused `CreatePersonPreProcessor` left behind).
- [ ] The endpoint is documented for Swagger with the request schema and the 201/400/409 responses (and 415 for a non-JSON content type if the framework returns it).
- [ ] UI: `StargateApi` gains `createPerson(name)` with request and result types in `models.ts`; Vitest tests with `HttpTestingController` cover the request body shape, success (201), 400 and 409 handling.
- [ ] Tests cover: success, location header, trimming, each invalid-input case, duplicate (exact, case and whitespace variants), unique-constraint race, no astronaut rows created, and no message leakage.

## Out of scope
- Renaming a person, the unique index migration, and the case-insensitive uniqueness rule itself (task 002).
- Listing and filtering people (task 001).
- Creating duties, ranks or titles (that is `POST /AstronautDuty`).
- Removing the try/catch-and-`ex.Message` pattern from other actions or a global exception handler (only this action must not leak).
- A "new person" form or screen in the UI. Only the API client method is in scope.
- Authentication (tracked with the API-key work).
- Importing people from the external feed.

## Notes
Depends on: 002

Why: this task relies on the case-insensitive unique index on `Person.Name` and the shared name rules (trim, length, allowed characters) that 002 defines, so there is one definition of a valid name for create and rename. 002 itself depends on 003 and 005, so the clean migration chain and warning-free build arrive transitively; they are not listed again. 001 and 004 are not prerequisites (004 only helps manual QA).

Relationship to task 002: resolved. 002 now delivers only the unique index and the rename (and defines the shared name rule); this task owns every `POST /Person` behaviour, including the duplicate 409. 002's wording was updated to match.

Breaking change: moving from a bare string body to an object body, and from 200 to 201, will break any existing caller. There is no `createPerson` in the UI today, so the in-repo impact looks limited to the Swagger consumers and any external feed; accepted by the user (see Decisions).

Observation: there is no `GET /Person/{id}` route. `GET /Person/{name}` is by name, and `PUT /Person/{personId}` (002) is by id.

Domain rules involved: 1 (Name unique), 2 (no assignment means no astronaut rows).
