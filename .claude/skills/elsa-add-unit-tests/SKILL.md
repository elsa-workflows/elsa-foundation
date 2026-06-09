---
name: "elsa-add-unit-tests"
description: "Plan and add Elsa unit tests required by the constitution. Use when a feature class, service registration, logic-bearing implementation, failure path, infrastructure exception wrapper, or branch behavior is created or changed; also use implicitly after implementation work to check whether feature registration or implementation unit tests are required."
argument-hint: "Test target or code change"
compatibility: "Requires elsa-foundation tests and constitution gates"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#testing-skills"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#add-feature-registration-tests` and `docs/skills/catalog.md#add-implementation-unit-tests`.
2. Read framework unit-test gates for feature registration and logic-bearing implementations.
3. Identify whether the change needs registration tests, implementation tests, or both.
4. Inspect nearby tests and existing test project references before proposing new test structure.
5. Plan focused tests with stubbed dependencies, meaningful branch coverage, and exception-boundary coverage where applicable.
6. If implementation was already approved and tests are direct follow-through, add the tests; otherwise return the plan first.

Do not use future integration tests as a substitute for required unit tests.
