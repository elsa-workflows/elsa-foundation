---
name: "elsa-auto-review-merge"
description: "Review a branch or pull request to convergence with independent reviewers and adversarial verification, then merge only on a fully green gate with evidence posted as a PR comment. Use when a branch or PR is ready for review, when review fixes need re-reviewing, or when a user asks to review and merge."
argument-hint: "Branch, PR number, or review focus"
compatibility: "Requires elsa-foundation source, gh CLI, and the .NET SDK"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#auto-review-loop-then-merge"
user-invocable: true
disable-model-invocation: true
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#auto-review-loop-then-merge` and `docs/skills/catalog.md#merge-gate`, plus `AGENTS.md#program-bookkeeping` for the merge rules.
2. Scope the diff first: use the PR base from `gh pr view --json number,url,baseRefName,headRefName,isDraft`, else the merge base with `main`. For a stacked PR, review only the commits this PR adds on top of its base, and state which base was used.
3. Run independent reviewers over that diff, one per lens, without shared context: correctness, repo-standards conformance against `AGENTS.md` and both files under `.specify/memory/`, and spec/issue conformance where the branch names one.
4. Verify each finding adversarially against the code before acting: discard any claim without a specific line and a concrete failure scenario, and mutation-test a guard before trusting it.
5. Apply surviving findings, rebuild, re-run affected suites as whole projects, and refresh generated maps when files or project references changed, staging every changed map by explicit path including `docs/maps/manifest.json`.
6. Re-derive the diff and loop from step 3 until a round produces no surviving findings, or until the stated iteration cap; report a capped run as unconverged, not as a pass.
7. Merge only through the Merge Gate: refusing is the default, the evidence comment must be posted on the PR first, and a red required check, a missing check run, a draft, or an unmerged base stops the merge even when the cause lies outside this branch.

Report scope, findings, discarded claims, and gate evidence. Do not merge without the posted PR comment.
