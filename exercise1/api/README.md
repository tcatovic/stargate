# Stargate API

ASP.NET Core Web API (`StargateAPI.csproj`, .NET 10) backed by SQLite. Run all commands from `exercise1/api` unless stated otherwise.

## Prerequisites

- .NET SDK as pinned in `global.json` (10.0.100, rolls forward to the latest feature band).
- The EF Core CLI: `dotnet tool install --global dotnet-ef --version 10.0.13`

## Database

The database is the SQLite file `exercise1/api/starbase.db`. It is gitignored and never committed. The API does **not** create or migrate it on startup (task 004 will change that), so create it yourself before the first run.

A relative `Data Source` is resolved against the API project folder, not the current directory. `dotnet ef`, `dotnet run` and the built `StargateAPI.dll` therefore all use the same file, whatever folder you start them from.

### Create or upgrade

```bash
cd exercise1/api
dotnet ef database update
```

From the repository root: `dotnet ef database update --project exercise1/api`.

On a clone without `starbase.db` this creates the file with the `Person`, `AstronautDetail` and `AstronautDuty` tables. Running it again on an up-to-date database changes nothing ("No migrations were applied"). A 0-byte `starbase.db` is fine; run the same command.

### Reset

Stop the API, then:

```bash
rm -f starbase.db starbase.db-shm starbase.db-wal
dotnet ef database update
```

### Inspect

```bash
dotnet ef migrations list
dotnet ef dbcontext info
dotnet ef migrations has-pending-model-changes   # must report no changes
sqlite3 starbase.db ".schema"                    # optional, if sqlite3 is installed
```

### Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `no such table: Person` | The database was never migrated. Run `dotnet ef database update`. |
| `starbase.db` is 0 bytes | Something opened it before it was migrated. Run `dotnet ef database update`. |
| `MakeCareerStartDateRequired: AstronautDetail has rows with NULL CareerStartDate...` | A stale development database from before `CareerStartDate` became required. The migration changed nothing. Reset (above). |
| `Database directory '...' does not exist` or `...is relative and this build does not embed the API project folder` | Set an absolute path (below). |

### Using a different file

Set `ConnectionStrings__StarbaseApiDatabase` (environment variable) or `--ConnectionStrings:StarbaseApiDatabase=...`, for example `ConnectionStrings__StarbaseApiDatabase="Data Source=/var/lib/stargate/starbase.db"`. Absolute paths are used as is; relative paths are still anchored to the API project folder.

The API project folder is embedded in **Debug builds only**. A Release build with a relative path fails at startup, so deployments must set an absolute path. `dotnet ef ... --connection "..."` bypasses this resolution for that one command.

### Notes

- Migrations after `InitialCreate` must be generated with `dotnet ef migrations add <Name> -o Business/Migrations --namespace StargateAPI.Migrations`. Review the generated `AlterColumn` for silent `defaultValue` backfills.
- The `AstronautDetail` migration rebuilds the table (SQLite cannot alter a column), so EF logs a warning about `PRAGMA foreign_keys = 0` not running in a transaction. That is expected.
- The NULL guard in the migration and the path resolution (`Business/Data/StarbaseDatabase.cs`) are SQLite-specific and transitional.
- Generating a SQL script (`dotnet ef migrations script`) with the NULL guard and running it in the `sqlite3` CLI has not been verified; use `dotnet ef database update`.

## Run

```bash
dotnet run          # Swagger at /swagger in Development
```

## Test

```bash
dotnet test ../api.Tests --collect:"XPlat Code Coverage"
```

Tests use their own temporary SQLite files and never touch `starbase.db`.

## Changelog (breaking changes)

- Task 003: `AstronautDetail.CareerStartDate` is now `NOT NULL` in the database. Databases holding rows with a NULL value must be reset. A relative `Data Source` now means "relative to the API project folder" instead of the working directory; a `starbase.db` kept elsewhere is no longer picked up.
