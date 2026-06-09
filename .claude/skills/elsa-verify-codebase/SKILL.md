---
name: "elsa-verify-codebase"
description: "Verify the Elsa codebase against selected constitution gates by inspecting references, extension-point catalogs, packages, tests, docs, stubs, and generated maps. Use when a user asks whether code complies with the constitution or wants a verification report."
argument-hint: "Gate, domain, or verification focus"
compatibility: "Requires elsa-foundation source, maps, and constitution files"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#verify-codebase-against-constitution"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#verify-codebase-against-constitution`.
2. Choose the relevant constitution gates and read both constitution layers when needed.
3. Inspect generated maps first, then source files only where maps are insufficient or freshness is uncertain.
4. Check project references, extension-point catalogs, package references, tests, source markers, and docs.
5. Classify each finding as code drift, doc drift, missing test, unclear gate, deferred implementation, or known exception.
6. Produce a report-shaped answer that can become a work unit.

Prefer producing or updating a report before proposing code changes.
