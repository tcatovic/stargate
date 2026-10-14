---
id: 005
title: Make the .NET build warning-free and enforce warnings as errors
status: in-progress   # draft | ready | planned | approved | in-progress | done | blocked
platforms: [api]   # api | ui | infra | db
---

## Problem
CLAUDE.md sets `dotnet build -warnaserror` (run from `exercise1/api`) as the build standard, but the API does not currently pass it. Nullable reference types are enabled in `StargateAPI.csproj`, and several nullability warnings exist. Nothing stops new warnings from being added, because the project does not treat warnings as errors by itself.

Warnings found by reading the source (the build itself was not run while writing this task, so the exact list and line numbers must be confirmed with `dotnet build --no-incremental`):

| # | Location | Warning | Kind |
|---|---|---|---|
| 1 | `Business/Data/AstronautDetail.cs`, `public virtual Person Person { get; set; }` | CS8618 non-nullable property uninitialized | annotation only |
| 2 | `Business/Data/AstronautDuty.cs`, `public virtual Person Person { get; set; }` | CS8618 | annotation only |
| 3 | `Business/Queries/GetAstronautDutiesByName.cs`, `GetAstronautDutiesByNameResult.Person` | CS8618 | annotation only |
| 4 | `GetAstronautDutiesByName.cs`, `result.Person = person;` (Dapper returns null when no row matches) | CS8601 possible null assigned | real bug: unknown name gives a null person |
| 5 | `GetAstronautDutiesByName.cs`, `person.PersonId` in the second query | CS8602 possible null dereference | real bug: unknown name throws NullReferenceException (HTTP 500) |
| 6 | `Business/Commands/CreateAstronautDuty.cs`, `person.Id` in the handler | CS8602 | real bug (latent): the handler re-reads the person with an interpolated query and assumes it exists; only the pre-processor check protects it |

Warnings 1-3 are model annotations. Warnings 4-6 are the compiler pointing at genuine missing null handling, in code that also contains SQL built by string interpolation and, in the duties flow, other defects owned by tasks 001 and the planned duties work.

## Outcome
`dotnet build -warnaserror` in `exercise1/api` succeeds with zero warnings, and the project fails the build on any future warning without needing the flag, so the CLAUDE.md standard stays true.

## Decisions (agreed with the user)
- **Revised scope (after plan review): this is a no-behaviour-change task.** Warnings 4-6 sit in handler code that no request reaches today (the GET route calls the person lookup, and the POST pre-processor rejects unknown names first), and the duties endpoints will be rewritten by the upcoming duties task. So warnings 4-6 get only the **smallest guard** that satisfies the compiler: the duties handler returns its default result (null `Person`, empty duties) on a null person, and the add-duty handler returns a 404-style result and writes nothing. The real 404 behaviour, the controller rewire, the pre-processor change (400 to 404) and the parameterised lookup are **deferred to the duties task** (listed in the plan's "Deferred" section).
- **NuGet audit warnings (NU1900-NU1904) stay warnings**, not errors, so a newly published advisory cannot break every build. A CI vulnerability gate is the place to enforce them. No compiler warning is suppressed.
- **`TreatWarningsAsErrors` goes into the build** once it is clean, so a plain `dotnet build` fails on any new warning.
- **It applies to every .NET project**, including `api.Tests`, through a `Directory.Build.props` under `exercise1/`, so new projects inherit it. If task 003 has merged, the test project is covered automatically; if not, the props file covers it when it appears.

## Acceptance criteria
- [ ] `dotnet build --no-incremental` in `exercise1/api` reports 0 warnings and 0 errors, and `dotnet build -warnaserror` succeeds.
- [ ] Every warning is fixed by correcting the code or its nullable annotations (for example `Person?` on navigation properties that may be absent, or `required` members where a value must always be supplied). No null-forgiving operator (`!`), no `#pragma warning disable`, no `[SuppressMessage]`, no `NoWarn` entry and no `<Nullable>` downgrade is introduced.
- [ ] `TreatWarningsAsErrors` is enabled for every .NET project under `exercise1/` (via `Directory.Build.props`), so a new warning fails a plain `dotnet build`. Confirmed by a deliberately introduced warning failing the build locally (not committed), including in `api.Tests` if it exists.
- [ ] Warnings 4-6 are removed by guards only in the currently-unreachable handler paths: `GetAstronautDutiesByNameHandler` returns its default result (null `Person`, empty duties) for a null person, and `CreateAstronautDutyHandler` returns a `CreateAstronautDutyResult` with `ResponseCode` 404, `Success` false and the fixed message "Person not found" (not echoing the name), writing nothing. Unit tests cover both guards and a success path. The controller, `CreateAstronautDutyPreProcessor`, its registration and all SQL are unchanged.
- [ ] API response shapes are unchanged: JSON property names and types of `GET /Person`, `GET /Person/{name}`, `GET /AstronautDuty/{name}` and `POST /AstronautDuty` match before and after for requests that succeed today.
- [ ] Runtime behaviour is unchanged for every request, including failing ones: before/after comparison on the seeded Development database of `GET /Person`, `GET /Person/{name}` and `GET /AstronautDuty/{name}` (known and unknown names) and a successful `POST /AstronautDuty`; `POST /AstronautDuty` with an unknown person still returns 400.
- [ ] No new migration is generated and `StargateContextModelSnapshot.cs` is unchanged. Making a navigation property nullable must not make a required relationship optional in the EF model (the foreign keys `PersonId` stay non-nullable). Verified with `dotnet ef migrations has-pending-model-changes` or an equivalent check.
- [ ] Existing tests still pass in Debug and Release, and the new guard tests and JSON-shape tests pass. No new packages.

## Out of scope
- Fixing SQL built by string interpolation (task 001 covers `GetPersonByName`; the duties handlers are separate work).
- Fixing the duties rules (3-7), the wrong query in the controller, the global exception handler, validation, or tests beyond what the scoping decision requires.
- Warnings in the UI (`exercise1/ui`) and any `dotnet format` or analyzer rule changes.
- Wiring the build into CI (no CI exists yet; see Notes).

## Notes
Depends on: none

Why: nothing here is a real prerequisite. It does overlap in files with task 001 (`GetPersonByName`, controllers and the `Person`/duty read paths) and with the planned duties work (`GetAstronautDutiesByName.cs`, `CreateAstronautDuty.cs`), and it may touch the entity files that task 002 also builds on. It should merge early, ideally before 001 and 002, so those branches start from a warning-free tree with `TreatWarningsAsErrors` already on and do not each have to resolve the same warnings or conflict on the same lines.

Domain rules involved: none directly. Warnings 4-6 sit in code that touches rule 2 (person with no assignment has no AstronautDetail/AstronautDuty rows) and the duty rules 3-7.

If this lands after the build has already started failing on `TreatWarningsAsErrors` in another branch, that branch must fix its own new warnings, not suppress them.
