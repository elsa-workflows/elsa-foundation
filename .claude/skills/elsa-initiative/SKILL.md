---
name: "elsa-initiative"
description: "Coordinate human-led planning on an initiative: an issue labelled `initiative` where technical architects turn analyses into ADR PRs, agreed decisions and specs. Use when a user works on, asks about or checks in to an initiative-labelled issue, or an issue or PR linked to one (sub-issue or `Part of #N`): where am I needed, contribute an analysis, report work, sync the board, or start an initiative. Not for work outside an initiative."
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
3. Find the user's role: the assignee is the coordinator, people in **Who is needed where** are technical architects, otherwise infer it from the request and never refuse.
4. Do the requested action as the catalog describes: check in, contribute an analysis, report work, sync (coordinator only) or start an initiative.
5. Apply the size guard before every post. Stop and offer the choices instead of working around a limit.

Only the coordinator edits the initiative's description. Do not touch `docs/program-goals/`.
