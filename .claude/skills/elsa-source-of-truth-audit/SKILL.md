---
name: "elsa-source-of-truth-audit"
description: "Audit Elsa-brain source-of-truth placement. Use when docs, constitution text, glossary terms, reports, maps, skills, specs, or code may duplicate responsibilities; when thinning constitution material; or when deciding where new knowledge/workflow/rules should live."
argument-hint: "Content, section, or proposed change"
compatibility: "Requires elsa-foundation AGENTS.md and docs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#source-of-truth-audit"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md#source-of-truth-layers` and `AGENTS.md#constitution-boundary`.
2. Read `docs/reports/knowledge-inventory.md`.
3. Read `docs/skills/catalog.md#source-of-truth-audit`.
4. Classify each piece of content as gate, glossary term, reference explanation, report finding, generated map, skill workflow, spec, or code.
5. Identify duplication, drift, missing canonical links, and material in the wrong layer.
6. Return a plan before moving or rewriting content.

Do not change constitutional meaning while moving explanatory material. After the user approves a relocation/thinning plan, small link/index updates that preserve meaning are normal follow-through.
