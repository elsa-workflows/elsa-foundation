---
name: "elsa-add-event-contribution"
description: "Plan or implement Elsa event contributions and independent event subscribers using the provider-neutral event/contribution workflow. Use when adding fan-in contributors, Source/Contributor/PreProcessor/PostProcessor implementations, action-named contributors, aggregating handlers, or non-fan-in event subscribers."
argument-hint: "Event contribution or subscriber description"
compatibility: "Requires elsa-foundation event gates and extension-point catalogs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#add-event-contribution"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#add-event-contribution` and, if relevant, `docs/skills/catalog.md#add-independent-event-subscriber`.
2. Read framework event/contribution gates and the owning domain's `EXTENSION_POINTS.md`.
3. Decide whether the change is a fan-in contribution or an independent subscriber.
4. For fan-in contribution, choose the contributor-interface kind and keep one owning aggregating event handler for that contribution purpose.
5. For independent subscribers, document handler behavior, registration, failure behavior, and tests.
6. Update extension-point catalogs and tests when implementing.

Important: event delivery strategy roles are still tracked as unfinished work. If publisher/subscriber ownership of strategy selection matters, stop and plan the event-handling strategy-role unit instead of guessing. After the user approves the event contribution/subscriber plan, required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through are part of completing the approved work.
