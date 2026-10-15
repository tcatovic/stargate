# Stargate API

ASP.NET Core Web API (`StargateAPI.csproj`, .NET 10) backed by SQLite. Run all commands from `exercise1/api` unless stated otherwise.

## Prerequisites

- .NET SDK as pinned in `global.json` (10.0.100, rolls forward to the latest feature band).
- The EF Core CLI: `dotnet tool install --global dotnet-ef --version 10.0.13`

## Database

The database is the SQLite file `exercise1/api/starbase.db`. It is gitignored and never committed. In the **Development** environment of a **Debug build** the API creates and migrates it on startup and seeds sample data (see [Automatic setup in Development](#automatic-setup-in-development)). In any other environment, and in every Release build even if it is started as Development, it does neither, so run `dotnet ef database update` yourself before the first run.

A relative `Data Source` is resolved against the API project folder, not the current directory. `dotnet ef`, `dotnet run` and the built `StargateAPI.dll` therefore all use the same file, whatever folder you start them from.

### Automatic setup in Development

When the build is a Debug build (`dotnet run`, `dotnet build` without `-c Release`) and `ASPNETCORE_ENVIRONMENT` is `Development` (all three `launchSettings.json` profiles, so `dotnet run` and the IDE), the API does this before it serves any request:

1. Applies every pending migration. A missing or 0-byte `starbase.db` is created.
2. If the `Person` table is empty, inserts the sample data below in a single transaction. If it already holds people, nothing is inserted; the seeder never merges or updates.

The console says what happened: `Applied N migration(s)` or `no migrations applied`, then `Seeded 6 people, 4 astronaut details and 8 duties` or `Seeding skipped`. If migration or seeding fails, the app logs a Critical entry and exits with an error instead of starting. A failed seed is rolled back (nothing seeded) and the next start retries it. The schema stays migrated.

Nothing of this runs in any other environment, and a Release build never does it, whatever the environment: it logs `This is not a Debug build; automatic migration and seeding skipped` and leaves the database alone (a deployed build must not auto-migrate or seed a real database by accident). `dotnet ef` commands never trigger it either. To start without it, run as Production: `dotnet run --no-launch-profile` (then use `dotnet ef database update` as below).

| Person | Demonstrates |
| --- | --- |
| Mira Okafor, Kenji Watanabe | No astronaut records |
| Tomas Lindqvist (CPT, Pilot), Priya Raman (MAJ, Mission Specialist) | One current duty, no end date |
| Elena Vasquez | Three duties (2LT Flight Engineer, CPT Pilot, MAJ Commander), each ending the day before the next starts; spans a leap day and a year boundary |
| Desmond Achebe | Retired: current duty `RETIRED` from 2023-04-01, career end 2023-03-31 |

Note: `GET /AstronautDuty/{name}` currently returns only the person summary (the controller calls `GetPersonByName`), not the duty list, so inspect the duties in the database (`sqlite3 starbase.db "SELECT p.Name, d.Rank, d.DutyTitle, d.DutyStartDate, d.DutyEndDate FROM AstronautDuty d JOIN Person p ON p.Id = d.PersonId ORDER BY p.Name, d.DutyStartDate"`).

### Create or upgrade (manual)

```bash
cd exercise1/api
dotnet restore        # first time on a fresh checkout; `dotnet ef` needs the restored project
dotnet ef database update
```

From the repository root: `dotnet ef database update --project exercise1/api`.

On a clone without `starbase.db` this creates the file with the `Person`, `AstronautDetail` and `AstronautDuty` tables. Running it again on an up-to-date database changes nothing ("No migrations were applied"). A 0-byte `starbase.db` is fine; run the same command.

### Reset

Development databases only; back up first for anything you care about (copy `starbase.db`). Stop the API, then:

```bash
rm -f starbase.db starbase.db-shm starbase.db-wal
dotnet run          # Development: migrates and reseeds
```

Outside Debug/Development, or to only create the schema, run `dotnet ef database update` instead. To get the sample data into a database that already has people, reset it as above; this deletes everything in it.

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
| `no such table: Person` | The database was never migrated (outside Debug/Development, or with `--no-launch-profile`). Run `dotnet ef database update`. |
| `Development database initialization failed during migration` | Startup could not apply migrations; the inner exception says why (for example the `MakeCareerStartDateRequired` row below). Fix the cause and restart. Data was not changed by the failed step. |
| `Development database initialization failed during seeding` | Nothing was seeded (rolled back), for example another process holds the file or a constraint was added that rejects the sample rows. Fix the cause and restart; seeding is retried while `Person` is empty. |
| `starbase.db` is 0 bytes | Something opened it before it was migrated. Run `dotnet ef database update`. |
| `MakeCareerStartDateRequired: AstronautDetail has rows with NULL CareerStartDate...` | A database from before `CareerStartDate` became required. The migration changed nothing. Safest: back up the file, set `CareerStartDate` on those rows, then re-run the update. For a disposable development database only: reset (above). |
| `AddUniquePersonNameIndex: some Person names are equal ignoring ASCII case and leading or trailing whitespace...` | Two people have names that collide once trimmed and compared ignoring ASCII case (for example `Ada` and `ada ` ). The migration changed nothing. List them with `sqlite3 starbase.db "SELECT trim(Name) COLLATE NOCASE AS n, group_concat(Id) FROM Person GROUP BY n HAVING COUNT(*) > 1"` (the migration also trims Unicode whitespace such as no-break and ideographic spaces, which `trim()` here does not, and aborts for a name that is empty once trimmed), then rename or merge those people (back up the file first), and re-run the update. For a disposable development database only: reset (above). |
| `Database directory '...' does not exist` (relative paths only) or `...is relative and this build does not embed the API project folder` | Set an absolute path (below). An absolute path to a folder that does not exist is not checked at startup: the app starts and the first request fails with `unable to open database file`, so create the folder first. |

### Using a different file

Set `ConnectionStrings__StarbaseApiDatabase` (environment variable) or `--ConnectionStrings:StarbaseApiDatabase=...`, for example `ConnectionStrings__StarbaseApiDatabase="Data Source=/var/lib/stargate/starbase.db"`. Absolute paths are used as is; relative paths are still anchored to the API project folder.

The API project folder is embedded in **Debug builds only**. A Release build with a relative path fails at startup, so deployments must set an absolute path. `dotnet ef ... --connection "..."` bypasses this resolution for that one command.

### Notes

- Migrations after `InitialCreate` must be generated with `dotnet ef migrations add <Name> -o Business/Migrations --namespace StargateAPI.Migrations`. Review the generated `AlterColumn` for silent `defaultValue` backfills.
- The `AstronautDetail` migration rebuilds the table (SQLite cannot alter a column), so EF logs a warning about `PRAGMA foreign_keys = 0` not running in a transaction. That is expected.
- The NULL guard in the migration and the path resolution (`Business/Data/StarbaseDatabase.cs`) are SQLite-specific and transitional.
- `AddUniquePersonNameIndex` first checks for names that collide ignoring ASCII case and surrounding whitespace and aborts, changing nothing, if it finds any. Otherwise it trims existing names (any Unicode whitespace at either end, the set .NET `string.Trim()` removes), makes `Person.Name` `COLLATE NOCASE` and adds the unique index `IX_Person_Name`. Down drops the index and the collation; trimmed names stay trimmed. Existing names are not NFC-normalised (SQLite cannot do it), so a pre-existing decomposed name stays as stored. Since task 006 the API trims ASCII whitespace only and rejects non-ASCII names; the migration's wider trim applied once to existing rows.
- Generating a SQL script (`dotnet ef migrations script`) with the NULL guard and running it in the `sqlite3` CLI has not been verified; use `dotnet ef database update`.

## Run

```bash
dotnet run          # Swagger at /swagger in Development
```

## Build

Warnings are errors: `exercise1/Directory.Build.props` sets `TreatWarningsAsErrors` for every .NET project under `exercise1/` (including `api.Tests` and any future project), so a plain `dotnet build` fails on any new warning. Fix the code; do not add `NoWarn`, `#pragma warning disable` or `!`. A newer SDK that adds analyzer warnings can also fail the build until they are fixed.

The one exception is the NuGet audit codes NU1900-NU1904 (feed unreachable, and low to critical package advisories). They are currently **warnings only**: they appear in the build output but do not fail the build, because they come from an external feed that changes without a commit. **No CI exists yet to gate vulnerable packages**, so until one does, a vulnerable dependency is visible only in the build output. To turn them back into errors on demand, override the exemption on the command line (a command-line property wins over the props file):

```bash
dotnet build -p:WarningsNotAsErrors=NU1900   # NU1901-NU1904 now fail the build; NU1900 (feed unreachable) stays a warning
```

Verified with `SharpZipLib 1.3.0` added to a scratch copy: a plain build succeeded with NU1902/NU1903 warnings, and the command above failed with `error NU1902` and `error NU1903`. (`dotnet list package --vulnerable` is not a gate: it exits 0 even when it finds vulnerabilities.)

## Test

```bash
dotnet test ../api.Tests --collect:"XPlat Code Coverage"
```

Tests use their own temporary SQLite files and never touch `starbase.db`. The automatic Development behaviour is tested by calling `DevelopmentDatabaseInitializer` directly (and through a service provider for the `Program.cs` wiring); the full `Program.cs` startup is checked by running the app.

## Changelog (breaking changes)

- Task 006 (BREAKING, C2): `GET /Person?name=` is now a case-insensitive **exact** match on the full name (it was a contains match). `?name=ra` and `?name=elena` now return an empty list; `?name=elena%20vasquez` returns Elena Vasquez. `rank` and `dutyTitle` are unchanged.
- Task 006 (BREAKING, C1): person names (and the `name`, `rank` and `dutyTitle` values checked by the same rule) are **ASCII-only**: letters A-Z and a-z, digits, space, hyphen, apostrophe and period, 1 to 100 characters after trimming. Any other character returns 400 on `GET /Person`, `GET /Person/{name}`, `PUT /Person/{personId}` and `POST /Person`. That includes accented letters such as `Émile`, which were valid before, NFD input, and non-ASCII whitespace such as a no-break space, even at the start or end. Only ASCII whitespace (space, tab, CR, LF) is trimmed. Names are no longer NFC-normalised. The validation message now reads "Use only English letters (A-Z), digits, ...". Names already stored with non-ASCII characters remain and are listed, but cannot be looked up by name; rename them by id.
- Task 002 (new endpoint): `PUT /Person/{personId}` (request body limited to 4096 bytes: larger bodies are refused with 413) with body `{ "name": "..." }` renames a person. 200 returns `{ success, message, responseCode, person }` (the same person shape as `GET /Person`); 404 unknown id; 400 invalid input; 409 when another person already has that name; 415 when the content type is not JSON. Names are NFC-normalised and trimmed, and unique ignoring ASCII case (SQLite `NOCASE`; `Émile` and `émile` are different names). Renaming to your own name or another casing of it succeeds. Idempotent.
- Task 002 (not breaking for ASCII input): the shared name rule used by `GET /Person` (`name`, `rank`, `dutyTitle`) and `GET /Person/{name}` is stricter: invisible letters and marks (U+034F, U+115F, U+1160, U+17B4, U+17B5, U+180B-U+180F, U+3164, U+FE00-U+FE0F, U+FFA0), a combining mark that does not follow a letter, and more than 3 combining marks in a row now return 400 (they used to return 200 with no match). The 400 shape and message are unchanged. These endpoints now NFC-normalise their input, so a decomposed query (`e` + U+0301) finds a precomposed stored name. Superseded by task 006 (C1): names are ASCII-only and no longer NFC-normalised.
- Task 002 (BEHAVIOUR CHANGE, interim until task 006): `POST /Person` is unchanged in code, but the database now has a unique case-insensitive index on `Person.Name`. Creating a person whose name equals an existing one (ignoring ASCII case) now fails with the existing generic handler: HTTP 500 `{ "success": false, "message": "An error occurred while saving the entity changes. See the inner exception for details.", "responseCode": 500 }` instead of 200 and a duplicate row. `POST /Person` still does not trim or normalise, so a padded name such as `"Ada "` can still be inserted next to `Ada`. Task 006 fixes both.
- Task 002 (data): the `AddUniquePersonNameIndex` migration trims existing names and adds the unique index; it aborts, changing nothing, when names collide or one is blank (see Troubleshooting).
- Task 002 (not breaking): route and query binding errors no longer echo the submitted value in the 400 body (`The value is not valid.`).
- Task 001 (BREAKING): `GET /Person/{name}` now returns HTTP 404 (`success: false`, `message: "Person not found"`, `responseCode: 404`, `person: null`) for an unknown name instead of 200 with `person: null`. The lookup is exact and case-insensitive (ASCII). Invalid input (longer than 100 characters, or characters other than letters, digits, space, hyphen, apostrophe, period) now returns 400 `ValidationProblemDetails`; a blank path name is 400. Unhandled errors on these two actions are no longer returned as a 500 body containing the exception message.
- Task 001 (BREAKING, second change): `GET /AstronautDuty/{name}` shares the person lookup, so an unknown name there now also returns 404 (`success: false`, `message: "Person not found"`, `responseCode: 404`, `person: null`; it was 200 with `person: null`) and the name match is now case-insensitive (ASCII). That endpoint's controller is unchanged: it still has no input validation and still returns exception text in the body of a 500 (pre-existing; tracked for the duties task).
- Task 001 (not breaking): `GET /Person` accepts optional `name` (case-insensitive contains), `rank` and `dutyTitle` (case-insensitive exact, current values) query filters, combined with AND. People with no astronaut assignment are only excluded by a rank or dutyTitle filter. Case-insensitivity folds ASCII letters only. The response shape is unchanged. Superseded by task 006 (C2): `name` is exact.
- Task 004 (not breaking): in Development, startup now migrates the database and seeds sample people when `Person` is empty. No API shape change.
- Task 003: `AstronautDetail.CareerStartDate` is now `NOT NULL` in the database. Databases holding rows with a NULL value must be reset. A relative `Data Source` now means "relative to the API project folder" instead of the working directory; a `starbase.db` kept elsewhere is no longer picked up.
