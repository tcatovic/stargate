---
id: 002
title: Add an endpoint to rename a person
status: planned
platforms: [api, db, ui]
---

## Problem
The spec (exercise1/README.md) requires "Add/update a person by name", but only creation exists (`POST /Person`, which takes a bare JSON string as the name). There is no way to update a person. Also, `Person.Name` has no unique index in the schema or migrations; uniqueness (rule 1) is only checked in `CreatePersonPreProcessor` by a read-then-write, so concurrent callers (including the external feed) can create duplicates. An update would hit the same race.

## Outcome
A caller can rename an existing person through a RESTful endpoint, and the system rejects any create or update that would break rule 1, both in code and in the database.

## Decisions (agreed with the user)
- "Update a person" means **rename**. `Person` has only `Id` and `Name`; rank and duty title change only through duties (rules 3-5).
- Endpoint: `PUT /Person/{personId}` with body `{ "name": "..." }`. Keyed by id, not name.
- Name uniqueness is **case-insensitive and whitespace-trimmed**. Names are trimmed before they are stored or compared.
- The unique-index migration **fails loudly** with a clear message if duplicate names already exist, and does not alter data.
- `POST /Person` is not changed here at all (body, status codes and duplicate handling belong to task 006).
- **Name rule** (shared with 006): 1 to 100 characters after trimming; letters, digits, spaces, hyphen, apostrophe and period only. Define it once here so create and rename use the same validation.
- No authentication in this task (tracked with the API-key work).
- Person responses keep the current shape.

## Acceptance criteria
- [ ] `PUT /Person/{personId}` with `{ "name": "..." }` renames the person and returns 200 with the updated person.
- [ ] Updating an unknown `personId` returns 404.
- [ ] Invalid input per the shared name rule (empty or whitespace, over 100 characters, disallowed characters, missing body) returns 400 and changes nothing.
- [ ] A rename that matches another person's name, ignoring case and surrounding whitespace, returns 409 and changes nothing (rule 1). Renaming a person to their own name, or to a different casing of it, succeeds.
- [ ] A new migration adds a unique, case-insensitive index on `Person.Name`; existing migrations are not edited. If duplicates already exist the migration fails with a clear error and alters no data.
- [ ] A race between two concurrent conflicting renames results in one success and one 409, never two rows with the same name. A test demonstrates the database constraint, not just the pre-check. (Duplicate handling for `POST /Person` is task 006.)
- [ ] Renaming does not alter the person's `AstronautDetail` or `AstronautDuty` rows.
- [ ] Repeating the same rename is idempotent (same final state, same success status).
- [ ] The endpoint is documented for Swagger with its 200/400/404/409 responses; `responseCode` equals the HTTP status.
- [ ] UI: `StargateApi` gains an `updatePerson(personId, name)` method with matching types in `models.ts`; Vitest tests with `HttpTestingController` cover success, 404 and 409 handling.
- [ ] Tests cover success, not found, invalid input, duplicate name (case and whitespace variants), the unique constraint, and idempotency.

## Out of scope
- Listing and filtering people (task 001).
- Changing duties, ranks or titles (that is `POST /AstronautDuty`).
- Any change to `POST /Person` (body, status codes, duplicate handling): task 006.
- An edit form in the UI. Only the API client method is in scope.

## Notes
Depends on: 003, 005

Why: the new unique-index migration must be generated on a clean migration chain (003 fixes the CareerStartDate drift and documents database creation), and 005 leaves the entity and handler files warning-free with `TreatWarningsAsErrors` on, so this task starts from a clean build. Task 004 (dev seed) is useful for QA but not required.

**Security finding from task 001's review (must be handled here, before the rule is used for writes):** the shared `InputRules` character rule uses `\p{L}` / `\p{M}`, which (a) accepts invisible letters such as U+3164 HANGUL FILLER, U+115F, U+1160 and U+FFA0, and (b) allows the same accented character in two encodings (NFC `é` vs NFD `e` + combining mark) with no normalisation. Reads only miss, but on create/rename it lets two visually identical people through a `COLLATE NOCASE` unique index (rule 1). This task (and 006) must NFC-normalise the name before validating, storing and comparing, reject the invisible fillers, and test both. That is a change to the shared rule, so the architect should plan it and the user should approve it.

Domain rules involved: 1 (Name unique), 2 (no assignment means no astronaut rows).
