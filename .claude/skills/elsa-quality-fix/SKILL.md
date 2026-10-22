---
name: "elsa-quality-fix"
description: "Run one pass of the recurring quality fixer: check for a reverted auto-fix PR, service open PRs labeled auto-fix, then claim one ready issue per Issue Bus Lite, fix it on an agent/claude branch, open a ready PR labeled auto-fix with evidence, request review, and stop. The routine never merges; Code Review does. Use when the fixer routine fires or a user asks for one fixer pass."
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

1. Read `docs/skills/catalog.md#quality-fix-routine`, `AGENTS.md#concurrent-work-claims`, and `docs/contributing/merging.md#pr-evidence`. Post as `elsa-workflows-claude[bot]` with no role prefix; only when posting through the owner's account, start every comment with `[Claude]`.
2. Check `main` for a revert of a PR labeled `auto-fix` since `auto-merge-paused` was last removed from #2559; if one is new, add the label with a comment naming it.
3. Service open PRs from `gh pr list --state open --label auto-fix`, fixing in new commits and replying `Fixed in <sha>` or `Won't fix: <reason>`; after 6 review rounds, comment `blocked: review loop exhausted` and add `needs:decision`. Stop when three remain open. Never approve or merge them.
4. Pick one eligible issue per the catalog rules (unassigned, sized S or M, not an epic, not blocked or `needs:decision`, not another worker's lane, no unabandoned claim, no open PR, no freeze window over its paths or kind of work).
5. Claim it: comment `claim: claude starting`, re-read, and if the earliest unabandoned claim is yours, assign it, remove `ready-for-agent` and set `status:in-progress`; otherwise comment `claim-abandoned` and pick again.
6. Branch `agent/claude/<n>-<slug>` from `origin/main`, implement only the issue, build touched projects, run their test projects whole, run the generated-maps check (`-- check`), and never skip, disable or quarantine tests. If blocked, comment `blocked: <reason>`, add `blocked` or `needs:decision`, and unassign.
7. Re-check for competing claims and new freeze windows before committing; then open a draft PR with `--draft --label auto-fix` and `Fixes #<n>` mapping each acceptance criterion to evidence, post the evidence as a PR comment, and comment `pr: <url>` on the issue. Once `Build & test` is green, run `gh pr ready <n>` so the required `Full tier` runs, before requesting review (`docs/contributing/merging.md#ci-tiers`).
8. Comment `@elsa-workflows-bot review` on the PR, unless #2559 is labeled `auto-merge-paused`, then stop. Code Review (`elsa-workflows-review-bot[bot]`) approves and merges once a round leaves no actionable findings and CI is green on the exact head commit.

Report whether `auto-merge-paused` is set, PRs serviced, the issue picked, the PR opened, and whether review was requested.
