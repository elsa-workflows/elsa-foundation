---
name: "elsa-extend-feature-inheritance"
description: "Plan an Elsa feature-inheritance extension before implementation. Use when one feature must extend, decorate, specialize, or override another feature's registration pipeline, including inherited feature classes, virtual ConfigureServices behavior, service additions/replacements, and registration tests."
argument-hint: "Feature inheritance scenario"
compatibility: "Requires elsa-foundation constitution, docs, and source layout"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#extend-feature-by-inheritance"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#extend-feature-by-inheritance`.
2. Read the framework feature-inheritance gate and related worked examples only as needed.
3. Identify the base feature, derived feature, dependency direction, registration override, and owned services.
4. Verify the base feature is public, inheritable, and has virtual registration.
5. Plan tests for registration behavior and service additions/replacements.
6. Return a plan with exact files and expected behavior before implementation.

Do not implement until the user approves the plan. After approval, required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through are part of completing the approved work.
