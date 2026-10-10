---
name: "elsa-quality-fix"
description: "Run one pass of the recurring quality fixer: check for a reverted auto-merge, service open auto-fix PRs, then claim one ready issue, fix it on a claude/auto-fix branch, open a ready PR with evidence, and auto-merge only small simplify/docs/tests changes outside runtime and persistence. Use when the fixer routine fires or a user asks for one fixer pass."
argument-hint: "Optional issue number to take instead of picking one"
compatibility: "Requires elsa-foundation source, gh CLI, and the .NET SDK"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#quality-fix-routine"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#quality-fix-routine`, `AGENTS.md#concurrent-work-claims`, and `docs/skills/catalog.md#merge-gate`.
2. Check `main` for a reverted auto-fix PR since `auto-merge-paused` was last removed from #2559; if one is new, add the label with a comment naming it.
3. Service open `claude/auto-fix-*` PRs; stop when three remain open.
4. Pick one eligible issue per the catalog rules (sized S or M, not an epic, not blocked, no unreleased claim, no open PR, no freeze window over its paths or kind of work), then claim it naming the worktree, branch and scope, and set `status:in-progress`.
5. Branch `claude/auto-fix-<n>-<slug>` from `origin/main`, implement only the issue, build touched projects, run their test projects whole, run the maps check, and never skip, disable or quarantine tests.
6. Re-check for competing claims and new freeze windows before committing; then open a ready PR with `Closes #<n>` and post the evidence as a PR comment.
7. Auto-merge through `elsa-auto-review-merge` only when every catalog condition holds; otherwise leave it for a human and say which condition failed.

Report whether auto-merge is paused, PRs serviced, the issue picked, the PR opened, and whether it merged.
