---
description: Have the architect plan a task (and write contracts if it spans platforms) on the task branch, then stop for approval
argument-hint: <task id, e.g. 003>
---

Plan task `$ARGUMENTS`.

1. Read `tasks/$ARGUMENTS-*.md`. It must have `status: ready`; if not, say what is missing and stop.
2. Create the task branch `task/<id>-<slug>` from `main` with `git switch -c` (an untracked or uncommitted task file comes along). If the branch already exists, switch to it. Never write plans on `main`.
3. Launch the `architect` agent with the task path. It writes `docs/plans/<id>-<slug>.html` and, if the verdict is `multi`, `docs/contracts/<id>/` including `contract.html`.
4. Set the task to `status: planned` and commit the task file, plan and contracts on the task branch: `docs(task-<id>): plan`.
5. Show the user the verdict, the plan path, the contract path (if any) and the architect's questions. Do not implement anything.

**Approval gate.** The user reviews the plan and contracts together. They approve by setting `status: approved` in the task file on this branch, or by telling you to. On changes, relaunch the architect with their feedback, recommit, and stop again.

Then the user runs `/lead $ARGUMENTS`.
