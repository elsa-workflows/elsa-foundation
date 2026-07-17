# Feature Specification: Runtime Volatile Wait Contract

> **Current status (2026-07-16): wait semantics remain historical input; all value transport is governed by [spec 095](../095-value-flow-redesign/spec.md).** A wait may not revive memory blocks, argument wrappers, ambient expression state, or generic value references.

**Feature Branch**: `codex/runtime-volatile-wait-contract`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Locked runtime execution addendum decisions for volatile waits.

## User Scenarios & Testing

### Primary User Story

As a runtime provider implementer, I need volatile waits to be represented as in-memory scheduler state and deterministic continuation work, so short request-scoped waits do not get confused with durable workflow suspension or bookmark resume.

### Acceptance Scenarios

1. Given an activity enters a volatile wait, when the runtime records the wait, then the registration is scoped to the workflow execution, activity execution, and branch.
2. Given a volatile wait completes, when continuation is represented in scheduler state, then it is a typed scheduler continuation work item rather than recursive callback bubbling or ordinary activity scheduling.
3. Given a runtime host evaluates a volatile wait, when policy is requested, then host support, requested duration, shutdown behavior, cancellation behavior, and durable fallback posture are explicit contract data.
4. Given runtime state is inspected, when volatile waits are present, then they do not carry bookmark IDs, resume target IDs, or C# callback names.

## Requirements

### Functional Requirements

- **FR-001**: Runtime MUST distinguish durable suspension from volatile wait in explicit contract vocabulary.
- **FR-002**: Volatile wait registrations MUST be scoped to `WorkflowExecutionId`, `ActivityExecutionId`, and optional `BranchId`.
- **FR-003**: Completed volatile waits MUST enqueue deterministic scheduler continuation work.
- **FR-004**: Scheduler continuation work MUST be separate from scheduled activity work.
- **FR-005**: Volatile wait contracts MUST expose host shutdown, cancellation, duration, and durable fallback policy inputs.
- **FR-006**: Volatile wait contracts MUST NOT persist bookmark IDs, resume target IDs, or C# callback method names.
- **FR-007**: Scheduler state MUST remain single-writer continuation state; this slice MUST NOT implement parallel activity execution.

### Non-Goals

- Full scheduler execution loop.
- Timer/event awaiter implementation.
- Durable bookmark store.
- Workflow pause/unpause control plane.
- Generator emission behavior.
- Full activity completion propagation behavior.

## Success Criteria

- Focused runtime tests prove volatile wait registrations and continuation work are typed, scoped, and distinct from durable suspension/bookmark resume.
- Runtime projects remain free of Design-owned execution-time dependencies.
- The extension-point catalog names the volatile wait policy boundary if introduced.
