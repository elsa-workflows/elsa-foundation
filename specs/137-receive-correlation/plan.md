# Implementation Plan: Receive Event Correlation

**Branch**: `codex/1001-receive-correlation` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/137-receive-correlation/spec.md`

## Summary

Make receive-side correlation an authored opt-in for an `Event` wait. A nonblank authored
correlation value is normalized and retained on the Event's existing wait registration; the
existing registration-to-bookmark metadata path and existing correlated-resume lookup then select
only matching waits. Unscoped delivery, start fan-out, BPMN authoring, and non-Event waits remain
unchanged.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)
**Primary Dependencies**: Existing Elsa Activities Runtime Core and Workflows Runtime Core contracts; no new dependency
**Storage**: Existing durable bookmark metadata; no schema, document-kind, or index change
**Testing**: xUnit, focused activity-runtime tests plus existing workflow-runtime lookup/router coverage
**Target Platform**: Cross-platform .NET hosts and CI
**Project Type**: Runtime activity library
**Performance Goals**: Constant-size metadata construction at Event wait registration; no additional reads, writes, scans, or indexes
**Constraints**: Preserve null/empty/whitespace broadcast; exact correlated resume matching; do not alter start fan-out, BPMN, non-Event waits, router, lookup, or persistence pipeline behavior
**Scale/Scope**: One existing activity behavior, its focused unit tests, and verification of existing runtime reader behavior

## Constitution Check

| Gate | Status | Evidence / plan response |
|---|---|---|
| Framework §2.21.1 test-objective preservation | PASS | Existing Event, bookmark lookup, and router objectives remain; tests are extended additively and none are removed. |
| Framework §2.23.2 direct branch coverage | PASS | Cover nonblank correlation, null/blank/whitespace absence, and matching/mismatching/unscoped reader outcomes. |
| Elsa §E2.2 Design ↔ Runtime boundary | PASS | No new Design dependency or deployment-shape change; work remains in existing Activities/Runtime composition. |
| Runtime durable-state evolution | PASS | Reuses an existing bookmark metadata key and pass-through channel; no persisted type or schema shape changes. |
| Constitution ratification status | ACKNOWLEDGED | The constitution is draft; scope stays narrow, additive, and reversible. |

**Pre-design result**: PASS — no exception or constitutional amendment is required.

## Research and Design Artifacts

- [research.md](research.md) resolves ownership, normalization, and non-goals.
- [data-model.md](data-model.md) records the existing metadata flow and compatibility rules.
- [quickstart.md](quickstart.md) defines focused validation commands and expected results.
- No `contracts/` directory is warranted: the public Event input and event-delivery surface already exist; this work changes receive-side behavior only and adds no endpoint, command, or external wire contract.

## Implementation Approach

1. At the Event wait-registration boundary, convert a nonblank authored correlation value into the existing runtime correlation metadata key; omit the key when the value is null, empty, or whitespace-only.
2. Preserve the existing registration-to-bookmark metadata propagation without changing the projector, scheduler handlers, bookmark creator, lookup, or router.
3. Extend focused Event tests to prove metadata emission and absence. Add an acceptance test that carries
   two actual Event registrations through durable bookmark persistence, global lookup, routing, and typed resume.
   Verify the existing lookup/router tests still demonstrate correlated narrowing, unscoped broadcast, and
   unchanged start-and-resume behavior.
4. Update Event-facing documentation only if its present wording still says correlation is passive or not retained by waits.

## Project Structure

### Documentation (this feature)

```text
specs/137-receive-correlation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
└── checklists/
    └── requirements.md
```

### Source and Tests

```text
src/Elsa/Activities/Primitives/
└── Activities/
    └── Event.cs

tests/Elsa/Activities/Runtime/Tests/
├── EventTriggerStimulusProviderTests.cs
└── EventCorrelationRoutingTests.cs

tests/Elsa/Workflows/Runtime/Tests/
├── GlobalBookmarkStimulusLookupTests.cs      # existing reader verification
└── StimulusRouterTests.cs                    # existing fan-in/start verification
```

**Structure Decision**: Change the authored Event wait at its existing registration boundary and
keep the existing runtime metadata pipeline as the sole propagation mechanism. No new project,
contract, persistence surface, or BPMN surface is introduced.

## Post-Design Constitution Check

PASS. The design writes one optional value through an existing immutable registration-metadata
channel, uses current durable bookmark metadata and lookup semantics, and preserves the test and
dependency boundaries identified above. No complexity exception applies.
