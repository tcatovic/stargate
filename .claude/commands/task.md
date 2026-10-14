---
description: Plan a task before coding
argument-hint: <what to do, e.g. fix SQL injection in person lookup>
---

Work on this task: `$ARGUMENTS`.

1. Read `CLAUDE.md`, `exercise1/README.md` and the code the task touches. If no description was given, ask what to work on.
2. Read every source file the task touches. Confirm the problem is real; if not, say so.
3. Present the plan, then stop for approval unless the user already said to proceed:
   - **Problem** (with `file:line` references and who/what is affected)
   - **Acceptance criteria** (testable bullets, tied to domain rules and the engineering standards)
   - **Change** (files and smallest edits; any migration or breaking API change called out explicitly)
   - **Tests** (unit/integration cases, including failure and hostile-input cases)
   - **Risk and rollout** (data impact, backward compatibility, how to roll back)
4. After approval: implement, run `/verify`, and commit (one task per commit, conventional message).

Ask before: destructive/data-altering migrations, breaking API changes, new dependencies, pushing or deploying.
