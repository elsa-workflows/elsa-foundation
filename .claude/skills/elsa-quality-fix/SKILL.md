---
name: "elsa-quality-fix"
description: "Run one pass of the recurring quality fixer: service open auto-fix PRs first, then claim one ready issue, fix it on a claude/auto-fix branch, open a ready PR with evidence, and auto-merge only small simplify/docs/tests changes outside runtime and persistence. Use when the fixer routine fires or a user asks for one fixer pass."
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
2. Service open `claude/auto-fix-*` PRs first; stop when three remain open.
3. Pick one eligible issue per the catalog rules (not blocked, not `size:L`, not claimed, no open freeze window over its paths), claim it, and set `status:in-progress`.
4. Branch `claude/auto-fix-<n>-<slug>` from `origin/main`, implement only the issue, build touched projects, run their test projects whole, and never skip, disable or quarantine tests.
5. Open a ready PR with `Closes #<n>` and post the evidence as a PR comment.
6. Auto-merge through `elsa-auto-review-merge` only when every catalog condition holds; otherwise leave it for a human and say which condition failed.

Report PRs serviced, the issue picked, the PR opened, and whether it merged.
