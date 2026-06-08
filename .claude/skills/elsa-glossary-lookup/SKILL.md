---
name: "elsa-glossary-lookup"
description: "Look up Elsa or modular-framework terminology from the canonical glossary. Use when a term's meaning is needed before continuing, when avoiding duplicated concept explanations, or when deciding whether content belongs in glossary, reference docs, constitution, reports, maps, skills, specs, or code."
argument-hint: "Term or phrase"
compatibility: "Requires elsa-foundation glossary and docs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#glossary-lookup"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#glossary-lookup`.
2. Check `docs/glossary/root.md`, then `docs/glossary/elsa.md`.
3. Read worked references only if the term needs deeper context for the task.
4. Return the meaning in this architecture, with links to canonical sources.

Do not create a parallel definition outside the glossary.
