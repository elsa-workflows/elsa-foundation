---
name: "elsa-initiative"
description: "Coordinate human-led planning on an initiative: an issue of type Initiative where technical architects turn analyses into ADR PRs, agreed decisions and specs on the initiative's integration branch. Use when a user works on, asks about or checks in to an Initiative issue, or an issue or PR linked to one (sub-issue or `Part of #N`): where am I needed, contribute an analysis, report work, reconcile a review or position, sync the board, reserve a number, merge an accepted proposal into the initiative branch, track a side track, land the initiative, or start one. Not for work outside an initiative."
argument-hint: "Initiative issue number and what you want to do"
compatibility: "Requires elsa-foundation docs and an authenticated gh CLI"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#initiative-coordination"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#initiative-coordination` and `docs/contributing/initiatives.md`.
2. Find the initiative: the issue given, or the parent of type Initiative (at any depth) or `Part of #N` of the work in hand. For an unlinked ADR or spec, check open initiative boards for its topic and add the missing link; ask if the scope is unclear. If there is no matching initiative, stop; this skill does not apply.
3. Find the user's role: the assignee (or the delegate the board records) is the coordinator, people in the board's **Approvers** list are approvers. Help with permitted contributions without requiring a role; decline actions reserved for a coordinator, recorded delegate or approver when the user lacks that role. Contribution, GitHub permissions or a bot's identity never grant approval authority.
4. Do the requested action as the catalog describes: check in, contribute an analysis, report work, reconcile a multi-point review or position, sync, reserve a number, merge an accepted proposal, land (these four coordinator or recorded delegate only), track a side track, or start an initiative. Proposal PRs target the initiative branch `initiative/<n>-<slug>` (stacked proposals target their parent's branch); authors cut their branch from it and pass the reserved spec number to Speckit with `--number`. Only the coordinator merges proposals (the delegate or a non-author approver when the coordinator is an author), and only after the guide's pre-merge gate: merge `origin/main` in (never rebase), check main's changes, and in a local unpushed merge of the PR head with the branch check the proposal against the whole branch and run `python tools/initiatives/check_decision_docs.py`; verify every finding, post one gate comment, let the approvers decide contradictions, then squash-merge once no contradiction, collision or check error is open. Never commit to a proposal. A side-track fix is its own issue outside the initiative; the proposal it blocks waits until the fix is synced in. Follow the guide's final-commit agreement rule for ADRs and specs; the author makes status edits, and material changes — ones that could seriously alter a reviewer's decision, not trivial edits such as the status line — require fresh agreement, with the re-review request stating what changed and what it means for that reviewer's prior answers. A reconciliation never makes anything agreed. During sync, open the required draft review PR for work merged without agreement and keep dependent work blocked.
5. Apply the size guard before every post: split into numbered comments or link a canonical record yourself; ask only when an alternative changes content, destination or access.

Only the coordinator, or the recorded delegate, edits the initiative's description. Do not touch `docs/program-goals/`.
