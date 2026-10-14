---
id: 004
title: Apply migrations and seed development data on startup in Development
status: planned
platforms: [api, db]
---

## Problem
Even with a working migration chain (task 003), a developer must run `dotnet ef database update` by hand and has no sample data to look at. The commented-out `StargateContext.SeedData` is dead code, and as written it uses `DateTime.Now` (non-deterministic, so `HasData` would generate a new migration every time) and bypasses the domain rules. An empty database makes `GET /Person` and the UI show nothing, and gives manual QA nothing to test against.

## Outcome
Running the API in the Development environment against an empty or out-of-date database brings it up to date and populates it with realistic sample people and duties that obey the domain rules. Nothing of this runs in any other environment.

## Decisions (agreed with the user)
- In **Development only**, the API applies pending migrations on startup. The explicit `dotnet ef database update` path from task 003 remains documented and working. Outside Development, migrations are never applied automatically.
- The seeder runs **automatically on Development startup when the database holds no people**. It is idempotent: running it again changes nothing.
- Seed data is inserted **directly** (not through `CreateAstronautDutyHandler`, which has known bugs and is about to change), with fixed dates computed to satisfy the rules.
- Seed contents: about six fictional people (not John Doe and Jane Doe) covering every case below.
- The commented-out `SeedData` in `StargateContext` is removed.

## Acceptance criteria
- [ ] In the Development environment, starting the API against a missing, empty or out-of-date `starbase.db` applies all pending migrations and then seeds, with no manual step.
- [ ] In any other environment, startup applies no migrations and seeds nothing; a test or documented check shows this.
- [ ] The seeder does nothing when the database already holds people. Starting the API twice yields identical data, with no duplicate people or duties.
- [ ] Seed data includes: at least one person with no astronaut records (rule 2); two people each with a single current duty with no end date (rules 3 and 4); one person with several duties where each earlier duty ends the day before the next starts and only the latest has no end date (rules 3 and 5); one retired person whose current title is `RETIRED` and whose `CareerEndDate` is one day before the retired duty's start date (rules 6 and 7). Names are unique (rule 1). Dates are fixed, not `DateTime.Now`.
- [ ] `AstronautDetail` rows agree with the latest duty for each seeded astronaut (current rank, current duty title, career start date).
- [ ] A test checks that the seeded data satisfies rules 1 to 7.
- [ ] After a clean start in Development, `GET /Person` and `GET /AstronautDuty/{name}` return the sample data against the running API.
- [ ] Startup logs state clearly whether migrations were applied and whether seeding ran or was skipped.
- [ ] If migration or seeding fails, the application fails to start with a clear error; it does not continue with a half-seeded database. Seeding runs in a single transaction.
- [ ] Tests do not depend on `starbase.db`; they use their own per-test database.
- [ ] The README or database docs from task 003 are updated to describe the automatic Development behaviour and how to reset.

## Out of scope
- SQL Server or Azure; the provider stays SQLite.
- Fixing duty-rule bugs in `CreateAstronautDutyHandler`, SQL injection in handlers, or global exception handling.
- Production migration strategy (migration bundles, release pipeline).
- Committing a pre-built `starbase.db`.

## Notes
Depends on: 003

Why: startup migration and seeding need a migration chain that applies cleanly and a database path that `dotnet ef` and `dotnet run` agree on.

Domain rules involved: 1 to 7 from `exercise1/README.md`.
Observation, not part of this task: `CreateAstronautDutyHandler` sets `CareerEndDate` to the retired start date (not the day before) when a person's first-ever duty is `RETIRED`, and to the day before when a detail already exists. That is inconsistent with rule 7 and belongs in the duty-rules task.
