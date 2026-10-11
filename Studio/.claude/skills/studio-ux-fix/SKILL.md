---
name: "studio-ux-fix"
description: "Run one pass of the UX fix routine: service open claude/ux-fix PRs, then claim one ready-for-agent + auto-qa issue filed by elsa-foundation's UX QA routine, turn its reproduction into a failing test, fix it, and open a ready PR with evidence for a human merge. Use when the UX fix routine fires or a user asks for one fixer pass."
argument-hint: "Optional issue number to take instead of picking one"
compatibility: "Requires pnpm, Node 22+, gh CLI, and the .NET SDK; an elsa-foundation checkout when a reproduction needs the real backend"
metadata:
  author: "elsa-foundation-studio"
  source: "docs/agents/ux-fix-routine.md"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/agents/ux-fix-routine.md` and `docs/agents/issue-tracker.md`.
2. Service open `claude/ux-fix-*` PRs; stop when two remain open.
3. Pick one eligible `ready-for-agent` + `auto-qa` issue (sized S or M, no unreleased claim, no open PR), claim it naming the branch and scope, and add `status:in-progress`.
4. Branch `claude/ux-fix-<n>-<slug>` from `origin/main` and turn the issue's reproduction into a failing test before changing product code.
5. Fix only the issue; run lint, typecheck, the unit suites and the affected browser specs; never skip, disable or quarantine tests.
6. Open a ready PR with `Closes #<n>`, post the evidence including the red-then-green transition, and leave the merge to a human.

Report the PRs serviced, the issue picked, and the PR opened.
