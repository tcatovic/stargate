---
description: Have the architect plan a task (and write contracts if it spans platforms), then stop for approval
argument-hint: <task id, e.g. 003>
---

Plan task `$ARGUMENTS`.

1. Read `tasks/$ARGUMENTS-*.md`. It must have `status: ready`; if not, say what is missing and stop.
2. Launch the `architect` agent with the task path. It writes `docs/plans/<id>-<slug>.html` and, if the verdict is `multi`, `docs/contracts/<id>/` including `contract.html`.
3. Set the task to `status: planned`. Show the user the verdict, the plan path, the contract path (if any), and the architect's questions. Do not implement anything.
4. Approval gate: the user reviews the plan and contracts together. The user approves by setting `status: approved` in the task file (or telling you to). On changes, relaunch the architect with the feedback.

Then the user runs `/lead $ARGUMENTS`.
