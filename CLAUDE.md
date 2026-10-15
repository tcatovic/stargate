# Stargate ACTS (Astronaut Career Tracking System)

ACTS is the system of record for people who have served as astronauts and their duty assignments (rank, title, start/end dates). A master list of people and duties is updated by an external service on its own schedule, so the API must be safe under repeated, out-of-order and concurrent updates. Treat this as a production service: correctness of career data, security, observability and maintainability all matter.

Domain spec: `exercise1/README.md` (requirements and rules).

## Layout

```
exercise1/api/                 ASP.NET Core 8 Web API (StargateAPI.csproj)
  Controllers/                 Thin controllers -> IMediator. BaseResponse + GetResponse() map ResponseCode to HTTP status
  Business/Commands/           MediatR commands, handlers, pre-processors (CreateAstronautDuty only; CreatePerson validates in its handler)
  Business/Queries/            MediatR queries + handlers (GetPeople, GetPersonByName, GetAstronautDutiesByName)
  Business/Data/               EF Core entities (Person, AstronautDetail, AstronautDuty) + StargateContext
  Business/Dtos/               PersonAstronaut
  Business/Migrations/         EF Core migrations
exercise1/api.Tests/           (planned) xUnit unit + integration tests
exercise1/ui/                  Angular 21 SPA (standalone components, signals, Vitest); consumes the API
global.json, .editorconfig     Repo-wide SDK pin and formatting
```

This repo is a monorepo: the API and UI version together. A change to an API contract updates `exercise1/ui/src/app/api/` in the same commit.

Stack: .NET 10 (`global.json` pins SDK 10.0.100), MediatR 12, EF Core 10 + SQLite (`starbase.db`, connection string `StarbaseApiDatabase`), Dapper for reads, Swashbuckle 10. UI: Angular 21, TypeScript, SCSS, npm.

## Commands

`dotnet` may not be on PATH; use `bash -l -c "dotnet ..."`. If the SDK is missing, say so; never claim a build or test result you did not observe.

```bash
cd exercise1/api
dotnet build -warnaserror
dotnet run                                          # Swagger at /swagger in Development
dotnet ef migrations add <Name> -o Business/Migrations
dotnet ef database update
dotnet test ../api.Tests --collect:"XPlat Code Coverage"

cd exercise1/ui
npm install --legacy-peer-deps                      # sandbox npm 9/10 crashes resolving vitest peers without the flag
npm start                                           # http://localhost:4200, proxies /api -> http://localhost:5204
npm test -- --watch=false
npm run build
```

The UI calls the API at `/api/...`; `ng serve` proxies that to the API's `http` profile (`proxy.conf.json`). Run the API first. Angular 21 is used because the sandbox's Node (22.22.1) is below Angular 22's minimum.

## Domain rules (the spec)

1. A Person is uniquely identified by Name.
2. A Person with no astronaut assignment has no AstronautDetail / AstronautDuty rows.
3. A Person has exactly one current duty (title, start date, rank) at a time.
4. The current duty has no DutyEndDate.
5. When a new duty arrives, the previous duty's DutyEndDate = new DutyStartDate minus 1 day.
6. A Person is "Retired" when the duty title is `RETIRED`.
7. CareerEndDate = the Retired duty's DutyStartDate minus 1 day.

Names are ASCII only: trimmed, 1 to 100 characters of A-Z, a-z, 0-9, space, hyphen, apostrophe and period (one shared rule, `InputRules`). Matching on names is case-insensitive and exact. Non-ASCII names are rejected on purpose.

Rules must hold in the database as well as in code (unique indexes, constraints, transactions), because the external feed and API callers can race.

## Engineering standards

**Architecture.** Controller -> MediatR request -> handler; responses derive from `BaseResponse`. Cross-cutting concerns (validation, logging, transactions) go in MediatR pipeline behaviors or middleware, not copy-pasted into handlers or controllers. Keep controllers thin and free of try/catch; one global exception handler maps exceptions to ProblemDetails.

**API contract.** Correct HTTP semantics: 400 invalid input, 404 unknown person, 409 conflicts/duplicates, 422 for rule violations if chosen, 500 only for real faults, with no internal details in the body. Keep `BaseResponse.ResponseCode` and the HTTP status identical. Treat any change to a request/response shape as a breaking change and call it out. Document endpoints for Swagger.

**Data.** All schema changes via a new migration; never hand-edit existing migrations, `bin/`, or `obj/`. Multi-row writes happen in one transaction. No interpolated SQL, ever; use EF LINQ or parameterised Dapper. Dates are date-only (`.Date`); store UTC where a time exists. Use `AsNoTracking` on reads.

**Security.** Validate and bound every input (length, charset, allowed titles/ranks). Assume the API will be internet-reachable: plan authentication/authorization, restrict CORS to configured origins, keep Swagger Development-only, and never log secrets or full request bodies containing personal data. No secrets in `appsettings*.json`; use user-secrets or environment variables.

**Reliability and observability.** Structured logging (`ILogger` with message templates, correlation id per request); log successes and failures to the console and the database log table; add `/health` endpoints (liveness + DB readiness). Honour `CancellationToken` through handlers.

**Testing.** Every behaviour change ships with a test. Priority: duty-rule handler (rules 3-7), validation, queries, then controller/integration tests via `WebApplicationFactory` with SQLite in-memory. Overall coverage target is well above 50%, and the rules logic should be close to fully covered. Tests assert behaviour (status codes, persisted state), not implementation details.

**Frontend.** Standalone components, signals, `inject()`, new control flow (`@if`/`@for`). API types live in `ui/src/app/api/models.ts` and mirror the C# response classes (camelCase JSON); all HTTP goes through `StargateApi`, never directly from components. Every view handles loading, empty and error states, uses semantic and accessible markup, and has a Vitest test with `HttpTestingController`. No `any`.

**Code style.** Nullable is on; never silence with `!`. Build with warnings as errors. Match surrounding style, keep comments for non-obvious *why*, remove dead code (e.g. the commented `SeedData`) or move it to a dev-only seeder.

**Delivery.** Small, reviewable changes: one task per branch/commit, conventional commit messages. CI (build, test, coverage, format check) must pass before merge. Keep a `README` with run/migrate/test instructions and a changelog of breaking API changes.

## Documentation format

Default to HTML, not Markdown, for any document you author: plans, design notes, reports, runbooks, API docs, changelogs, review summaries. Write self-contained `.html` files (inline CSS, no external dependencies, readable on a phone, light and dark mode) and put them in `docs/`. Only write `.md` when a tool requires it or agents edit the file as text: this `CLAUDE.md`, `.claude/commands/*.md`, `.claude/agents/*.md`, task files in `tasks/`, `.github/pull_request_template.md`, and a `README.md` that GitHub renders. Plans and contract summaries are HTML (`docs/plans/`, `docs/contracts/<id>/contract.html`); machine contracts keep their native format (`openapi.yaml`, `types.ts`). Do not create new `.md` files otherwise, and when asked for a write-up, produce HTML.

## Workflow

Work flows through agents, with the user approving at fixed gates. Task creation is separate from this flow: the project-manager (`/pm <idea>`) or the user adds task files to `tasks/`, and `/advance` picks up any with `status: ready`. Task files live in `tasks/` (`NNN-slug.md`, from `tasks/_template.md`, with a `status` in the frontmatter).

```
tasks/NNN.md with status: ready          (added separately, see below)
     -> /plan NNN -> architect writes docs/plans/NNN.html (+ docs/contracts/NNN/ if multi)
        GATE 1: user approves plan and contracts together
     -> /lead NNN -> branch task/NNN-slug; developer agents implement; reviewers check; lead integrates;
                     qa-tester runs the app against the acceptance criteria (docs/qa/NNN.html)
     -> /ship NNN -> push branch, open PR
        GATE 2: user reviews and merges the PR
```

| Role | How it runs | Responsibility |
|---|---|---|
| project-manager (outside the flow) | subagent via `/pm`, writes only `tasks/` | Turns ideas into small, testable tasks; the flow never calls it |
| architect | subagent, read-only on code | Plans; decides `simple` vs `multi`; writes the contracts for multi tasks |
| tech lead | the main session via `/lead` | Orchestrates developers against the approved contracts, integrates, runs review; never edits contracts |
| api-dev, ui-dev, infra-dev | subagents, each scoped to its folders | Implement with tests; stop and report if a contract is wrong |
| security-reviewer, test-reviewer | read-only subagents | Review the diff before the PR |
| qa-tester | subagent, read-only on source, writes only `docs/qa/` | Runs the real app and tests each acceptance criterion; the PR is blocked unless the verdict is `pass` |

Run unattended with `/loop 30m /advance`: each tick advances one task one step (`ready` -> planned, `approved` -> built and shipped as a PR) and pauses at the gates until you set `status: approved` or merge the PR. Statuses: `draft | ready | planned | approved | in-progress | done | blocked`. A task's status lives in its task file on its `task/NNN-slug` branch: `/plan` creates the branch and commits the plan there, you approve by setting `status: approved` on that branch, and `/ship` sets `done` so it takes effect when you merge the PR. `main` only changes through merged PRs.

Rules:
- Dependencies: each task's Notes start with `Depends on: none` or task ids. A task is eligible to be planned or built only when every dependency is `done` on `main` (PR merged). `/plan`, `/lead` and `/advance` all enforce this, and `/advance` takes the lowest eligible id first.
- Simple tasks (one platform) skip contracts and go to a single developer agent. Multi tasks get contracts from the architect first, and parallel developers each work in their own git worktree.
- A contract defect goes back to the architect, and the user re-approves. Developers and the lead never rewrite contracts.
- Agents commit to a `task/NNN-slug` branch and open a PR; they never push to `main` and never merge. Commit once per logical change with conventional messages.
- Worktree isolation needs at least one commit in the repo, so make the baseline commit first.
- Hooks (`.claude/hooks/protect-files.sh`) block edits to build output and existing migrations.

Ask before: schema changes that alter or drop existing data, breaking API changes, adding dependencies, and anything outward-facing (push, deploy).
