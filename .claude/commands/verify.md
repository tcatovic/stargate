---
description: Build, test and review the current change against project standards
---

Verify the work just done.

1. From `exercise1/api`, run `bash -l -c "dotnet build -warnaserror"` and, if `exercise1/api.Tests` exists, `bash -l -c "dotnet test ../api.Tests --collect:'XPlat Code Coverage'"`. If the SDK is missing, report that and do not claim a pass.
2. List new warnings and any coverage drop in touched files.
3. Review the diff against `CLAUDE.md`: domain rules 1-7, no interpolated SQL, validation in the pipeline, correct HTTP status and ProblemDetails, transactions around multi-row writes, nullable honesty, no secrets or PII in logs, migrations are new files and reversible.
4. If the change touches endpoints and the app can run, exercise them with `curl`: happy path, unknown person, duplicate, malformed body, hostile input. Show the status codes.
5. Report: what passed, what failed (with output), what was not verified and why, and any breaking change or migration the reviewer must know about.
