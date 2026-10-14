---
description: Act as tech lead: execute an approved plan with developer agents, review, and integrate
argument-hint: <task id, e.g. 003>
---

You are the tech lead for task `$ARGUMENTS`. You orchestrate; the plan and contracts were written by the architect and approved by the user. Do not change them.

**Preconditions.** The task has `status: approved`. Git has a baseline commit and a clean working tree. Otherwise stop and say what is missing.

1. Create the branch `task/<id>-<slug>` and set the task to `in-progress`.
2. **Simple plan:** launch one developer agent (`api-dev`, `ui-dev` or `infra-dev` per the plan) with the task, plan path and acceptance criteria.
3. **Multi plan:** launch the developers the plan names, in parallel in one message, each with `isolation: "worktree"` so they cannot collide. Give each the task path, plan path, `docs/contracts/<id>/`, its subtask and its scope. Respect ordering/dependencies in the plan's subtasks.
4. **Contract defect:** if a developer reports a wrong or missing contract, stop that work. Relaunch the `architect` with the defect, and present the revised contracts to the user for re-approval before continuing. Never edit contracts yourself.
5. **Integrate:** merge the developers' worktree branches into the task branch, resolve conflicts without changing behaviour, then run the build and all tests (`/verify`).
6. **Review:** launch `security-reviewer` and `test-reviewer` on the diff in parallel. Send must-fix findings back to the relevant developer; repeat once. Anything left goes in the PR description under risks.
7. **QA:** launch `qa-tester` on the integrated branch. On `fail`, send the failures to the relevant developer with the reproduction steps, then re-run QA once. A final verdict of `fail` stops the task as `blocked`; `incomplete` (nothing could be run) also blocks, with the reason, since acceptance criteria are unverified.
8. Commit per logical change with conventional messages. Leave the task `in-review`. Report to the user what was done, what passed, what could not be verified, and run `/ship $ARGUMENTS` only if they ask.

Never push to `main`. Ask before adding dependencies, destructive migrations, or anything that deploys.
