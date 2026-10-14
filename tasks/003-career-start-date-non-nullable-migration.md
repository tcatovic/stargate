---
id: 003
title: Make AstronautDetail.CareerStartDate non-nullable and document database creation
status: planned
platforms: [api, db]
---

## Problem
A developer who clones the repo cannot get a working database.

- Nothing creates `starbase.db`. `Program.cs` registers `UseSqlite(GetConnectionString("StarbaseApiDatabase"))` (`Data Source=starbase.db`) but never applies migrations. The file is gitignored (`*.db`), so a fresh clone has none, and the first connection creates an empty file with no tables.
- The committed migration is out of sync with the entities. `AstronautDetail.CareerStartDate` is `DateTime` (non-nullable) in the entity, but `InitialCreate.cs` and `StargateContextModelSnapshot.cs` declare it nullable. It is the only drift found. The snapshot also carries `ProductVersion 7.0.15` while the project targets `net10.0` with EF Core 10.0.13. EF reports pending model changes and `dotnet ef database update` refuses to run.
- `Data Source=starbase.db` is relative to the working directory, so `dotnet ef` and `dotnet run` can end up using different files.
- There is no documented procedure to create or reset the database.

## Outcome
The model and the migration chain agree, `CareerStartDate` is non-nullable in the database, and following a short documented procedure on a fresh clone produces a working `starbase.db` that both `dotnet ef` and `dotnet run` use. Future migrations (starting with task 002) generate cleanly.

## Decisions (agreed with the user)
- Stay on SQLite. SQL Server and Azure are out of scope.
- **No backfill.** If existing rows have a NULL `CareerStartDate`, the migration fails with a clear message and alters no data. The documented remedy for a stale development database is to delete `starbase.db` and re-run the migrations.
- The database file lives at `exercise1/api/starbase.db`. The connection string is resolved against the API project's folder in code so `dotnet ef` and `dotnet run` always use the same file. An environment variable or configuration value may override it.
- The API does not apply migrations automatically in this task (task 004 handles startup behaviour and seeding).

## Acceptance criteria
- [ ] A new migration makes `AstronautDetail.CareerStartDate` non-nullable and brings the snapshot in line with the entities. `InitialCreate` and its Designer file are not edited.
- [ ] After the new migration, `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- [ ] If any `AstronautDetail` row has a NULL `CareerStartDate`, applying the migration fails with a clear message and leaves the data unchanged.
- [ ] The migration applies cleanly to a database that already holds the `InitialCreate` schema (with and without rows), and has a working `Down`, verified by applying and reverting it on a scratch database.
- [ ] On a fresh clone with no `starbase.db`, the documented command (`dotnet ef database update`) creates the database with the `Person`, `AstronautDetail` and `AstronautDuty` tables and the existing constraints (primary keys, foreign keys with cascade, unique index on `AstronautDetail.PersonId`).
- [ ] The procedure is repeatable: running it again on an up-to-date database succeeds and changes nothing.
- [ ] `dotnet ef` and `dotnet run` resolve to the same database file at `exercise1/api/starbase.db`, whatever directory they are started from. No second, empty file appears elsewhere.
- [ ] The documentation states the prerequisites (SDK, `dotnet-ef` tool), the commands to create, reset and inspect the database, the file location, and how to recover from a stale or empty `starbase.db`. It is reachable from `exercise1/README.md`.
- [ ] Tests (using their own per-test database, not `starbase.db`) check that the model has no pending changes against the migration chain, and that the connection string resolution points at the API project folder.

## Out of scope
- Applying migrations or seeding automatically on startup (task 004).
- The unique index on `Person.Name` and any `Person` changes (task 002).
- SQL Server or Azure; the provider stays SQLite.
- Fixing duty-rule bugs, SQL injection in handlers, or global exception handling.
- Committing a pre-built `starbase.db`.

## Notes
Depends on: none

Domain rules involved: none changed; this is schema alignment. Task 002 depends on this task (it adds a migration on top of a clean chain). Task 004 depends on it too.
