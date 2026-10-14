---
description: One tick of the task workflow, meant for /loop. Advances at most one step and never skips an approval gate.
---

Advance the task workflow by one step, then stop. Read every `tasks/NNN-*.md` and its `status`.

Pick the first match, in this order:

1. **A task `in-progress`**: another run may be active or crashed. Check `git branch` and `git status`. If the branch exists with work and the tree is clean, resume `/lead <id>`; if the state is unclear, set `status: blocked` with a note and stop.
2. **A task `approved`**: run `/lead <id>`. When it finishes with the build and tests passing and reviews done, run `/ship <id>` and set `status: in-review` (the PR is open). If the build or tests fail, or the SDK/Node is missing so nothing could be verified, set `status: blocked`, write the reason and evidence into the task's Notes, and do not ship.
3. **A task `ready`**: run `/plan <id>` and stop at the gate (`status: planned`). Do not implement.
4. **Nothing actionable** (only `draft`, `planned` awaiting approval, `in-review`, `done`, `blocked`, or no tasks): do nothing.

Rules:
- Work on one task at a time. If any task is `in-progress` or `in-review` with an open PR for the same files, prefer finishing that over starting another.
- Never approve a plan or contracts, merge, or push to `main`. Approval is the user setting `status: approved`; review is the user merging the PR. When a PR is merged (check `gh pr view`), set the task `done`.
- Never edit `draft` tasks; they belong to the user and `/pm`.
- End each tick with one line: the task id, what step ran, and the resulting status; or "nothing to do". Mention anything `planned` that is waiting for the user's approval, and anything `blocked`.

Run it with `/loop 30m /advance` (fixed interval) or `/loop /advance` (self-paced).
