# Feature Specification: Runtime Pipeline Slots And Inspectable Plans

**Feature Branch**: `codex/runtime-pipeline-slots`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Slice 4 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Register Runtime Middleware By Slot (Priority: P1)

A runtime module can register workflow or activity middleware into a stable named slot without depending on concrete neighboring middleware types.

**Independent Test**: Register custom workflow middleware into the scheduling slot and custom activity middleware into the invoke slot, then verify the resolved plan orders the entries by slot and order.

### User Story 2 - Inspect Resolved Runtime Pipeline Plans (Priority: P1)

A runtime maintainer can inspect the resolved workflow and activity pipeline plans before behavior-heavy execution is implemented.

**Independent Test**: Build workflow and activity plans and assert that each step exposes pipeline kind, slot name, middleware type, order, and built-in/custom status.

### User Story 3 - Keep Workflow And Activity Pipelines Separate (Priority: P1)

A runtime module cannot accidentally register workflow middleware into the activity pipeline or activity middleware into the workflow pipeline.

**Independent Test**: Compile-time generic constraints and reflection checks prove workflow and activity middleware use distinct context types and builder contracts.

## Requirements

- **FR-001**: Runtime.Core MUST define separate workflow and activity runtime pipeline middleware contracts.
- **FR-002**: Runtime.Core MUST define stable named slots for workflow and activity runtime pipelines.
- **FR-003**: Runtime.Core MUST define a registration contract using slot name and order.
- **FR-004**: Runtime.Core MUST define an inspectable pipeline plan model.
- **FR-005**: Runtime.Core MUST provide workflow and activity pipeline plan builders.
- **FR-006**: Runtime.Core MUST include built-in middleware placeholders for load state, scheduling, input evaluation, invoke, capture outputs, checkpoint, and post-commit phases.
- **FR-007**: Runtime.Core MUST explicitly defer before/after dependency constraints; this slice uses slot plus order only.
- **FR-008**: Runtime.Core MUST remain free of Design-owned authored workflow model dependencies.

## Out Of Scope

- Executing the runtime pipeline.
- Full scheduler behavior.
- Concrete middleware implementations beyond no-op placeholders.
- DI registration conventions.
- Before/after graph sorting.

## Success Criteria

- Tests prove slot-based registration and plan ordering.
- Tests prove workflow and activity contexts are distinct.
- Tests prove the built-in placeholder slots are inspectable.
