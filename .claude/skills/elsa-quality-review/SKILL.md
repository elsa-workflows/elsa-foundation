---
name: "elsa-quality-review"
description: "Run one pass of the recurring quality reviewer: one rotating lens and area, at most three needs-triage + auto-review issues with kind and size labels, duplicate check first, and no filing while 15 or more await triage. Use when the reviewer routine fires or a user asks for one review pass."
argument-hint: "Optional lens and area override"
compatibility: "Requires elsa-foundation source and gh CLI"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#quality-review-routine"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#quality-review-routine` and `docs/adr/0080-elsa-4-simplification-decisions.md#d7--recurring-review-and-fix-routines`.
2. Count open `auto-review` + `needs-triage` issues; at 15 or more, file nothing and report the count.
3. Derive this run's lens and area from the catalog's rotation, unless the user input names them.
4. Review that area through that lens from code, with `file:line` evidence; edit nothing and open no PRs.
5. Search open and closed issues for duplicates, then file at most three issues labelled `needs-triage`, `auto-review`, one `kind:*` and one `size:*`.

Report the lens, area, issues filed, and findings dropped.
