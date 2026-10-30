---
name: "elsa-initiative"
description: "Coordinate human-led planning on an initiative: an issue labelled `initiative` where technical architects turn analyses into ADR PRs, agreed decisions and specs. Use when a user works on, asks about or checks in to an initiative-labelled issue, or an issue or PR linked to one (sub-issue or `Part of #N`): where am I needed, contribute an analysis, report work, reconcile a review or position, sync the board, or start an initiative. Not for work outside an initiative."
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
2. Find the initiative: the issue given, or the `initiative`-labelled parent (at any depth) or `Part of #N` of the work in hand. If there is none, stop; this skill does not apply.
3. Find the user's role: the assignee (or the delegate the board records) is the coordinator, people in the board's **Approvers** list are approvers; otherwise infer what the user is doing and never refuse. Contribution, GitHub permissions or a bot's identity never grant approval authority.
4. Do the requested action as the catalog describes: check in, contribute an analysis, report work, reconcile a multi-point review or position, sync (coordinator only) or start an initiative. A reconciliation never makes anything agreed.
5. Apply the size guard before every post: split into numbered comments or link a canonical record yourself; ask only when an alternative changes content, destination or access.

Only the coordinator, or the recorded delegate, edits the initiative's description. Do not touch `docs/program-goals/`.
