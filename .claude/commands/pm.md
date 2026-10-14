---
description: Turn a rough idea into a task file in tasks/ with the project-manager agent
argument-hint: <rough idea>
---

Idea: `$ARGUMENTS`

1. Launch the `project-manager` agent with the idea. It drafts `tasks/<id>-<slug>.md` and returns open questions.
2. Show the user the draft summary and the open questions. Ask the questions, update the task file with the answers (re-run the agent if the scope changed), and iterate until the user is happy.
3. When the user confirms, set `status: ready` in the task file. Do not start planning; the user runs `/plan <id>` next.
