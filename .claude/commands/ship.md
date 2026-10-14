---
description: Push the task branch and open a pull request for the user's code review
argument-hint: <task id, e.g. 003>
---

Ship task `$ARGUMENTS`.

1. Confirm the current branch is `task/$ARGUMENTS-*`, never `main`, the tree is clean, and the task is `in-review`. Refuse otherwise, and refuse if `docs/qa/<id>-*.html` is missing or its verdict is not `pass`.
2. Run `git log --oneline main..HEAD` and `git diff --stat main...HEAD` and summarise.
3. `git push -u origin <branch>`. If it fails for authentication, give the user the sandbox secret command from the sandbox notes; do not work around it.
4. Open a PR with `gh pr create`, filling `.github/pull_request_template.md`: link the task, plan and QA report (`docs/qa/`), contracts changed, verification evidence, reviewer findings and what was not verified. Add the required PR attribution line.
5. Report the PR URL. Do not merge; the user reviews and merges.
