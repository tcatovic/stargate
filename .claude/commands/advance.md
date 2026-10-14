---
description: One tick of the task workflow, meant for /loop. Advances at most one step and never skips an approval gate.
---

Advance the task workflow by one step, then stop.

**Dependencies.** Every task has a `Depends on:` line at the top of its `## Notes` (`none`, or task ids such as `003, 004`). A dependency is satisfied only when that task's file on `main` has `status: done`, meaning its PR was merged. Check this before choosing a task:
- A `ready` or `approved` task with an unsatisfied dependency is **waiting**: skip it, never plan or build it, and name what it waits for in the tick report.
- An `in-progress` task continues regardless.
- A missing `Depends on:` line, an id with no task file, or a dependency cycle sets the task to `blocked` with a note.
- Among eligible tasks, take the lowest id first.

**Find the state.** A task's status lives in its task file on its own branch. For each `tasks/NNN-*.md` on `main`, check whether a `task/NNN-*` branch exists (`git branch --list 'task/*'`); if so, read the file from that branch (`git show <branch>:tasks/<file>`, or the working copy if that branch is checked out). Open PRs: `gh pr list --state open --json headRefName,url`.

Pick the first match, in this order:

1. **A task `in-progress`**: another run may be active or crashed. If its branch has commits and the tree is clean, resume `/lead <id>`; if the state is unclear, set `status: blocked` with a note on that branch and stop.
2. **A task `approved` with all dependencies satisfied**: run `/lead <id>`. When it finishes with build, tests, reviews and QA `pass`, run `/ship <id>`. If the build or tests fail, QA is not `pass`, or the SDK/Node is missing so nothing could be verified, set `status: blocked`, write the reason and evidence into the task's Notes, commit it on the branch, and do not ship.
3. **A task `ready` with no branch and all dependencies satisfied**: run `/plan <id>` and stop at the gate (`status: planned`). Do not implement.
4. **Nothing actionable**: do nothing.

Rules:
- Work on one task at a time. If a task has an open PR or is `in-progress`, finish or wait on that before starting another.
- Never approve a plan or contracts, merge, or push to `main`. Approval is the user setting `status: approved` on the task branch; review is the user merging the PR. A task whose PR merged shows `done` on `main`.
- Never edit `draft` tasks; they belong to the user and `/pm`.
- Always return to `main` (clean) at the end of a tick unless a branch has uncommitted user edits.
- End each tick with one line: the task id, what step ran, and the resulting status; or "nothing to do". Mention plans waiting for approval, open PRs awaiting review, tasks waiting on dependencies (and which), and anything `blocked`.

Run it with `/loop 30m /advance` (fixed interval) or `/loop /advance` (self-paced).
