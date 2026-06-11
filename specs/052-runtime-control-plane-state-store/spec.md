# Feature Specification: Runtime Control Plane State Store

**Feature Branch**: `codex/runtime-control-plane-state-store`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after control-plane pause/unpause terminology contracts exist. Add a narrow control-plane state store and pause-boundary decision provider without implementing administrative APIs or scheduler enforcement.

## Scenarios & Tests

1. Given control-plane pause state exists for a workflow execution, when runtime asks whether scheduler work may advance at a named boundary, then the default provider returns a blocked `SchedulerPauseDecision` for the matching hold.
2. Given no active hold matches the requested workflow/activity/generator/ingress/worker/host target, when runtime asks for a decision, then scheduler advancement is allowed.
3. Given runtime API services are composed, when `IControlPlaneStateStore` and `IRuntimePauseDecisionProvider` are resolved, then the default implementations are available and replaceable.

## Requirements

- **FR-001**: Runtime.Core MUST expose `IControlPlaneStateStore` as the storage boundary for administrative `ControlPlaneState`.
- **FR-002**: The default in-memory store MUST save, replace, find, list workflow-scoped, and list all control-plane states.
- **FR-003**: Runtime.Core MUST expose an overridable `IRuntimePauseDecisionProvider` that evaluates pause holds at named runtime boundaries.
- **FR-004**: The default provider MUST match effective holds by workflow execution, activity execution, generator, ingress source, worker, or host target without reading workflow continuation state.
- **FR-005**: When multiple holds match, the default provider MUST choose deterministically by oldest request time, then hold ID.
- **FR-006**: Allowed decisions MUST use an explicit non-paused continuation policy.
- **FR-007**: Activity and generator pause-decision requests MUST require a workflow execution ID because those hold scopes are workflow-owned.
- **FR-008**: Runtime API composition MUST register the store and provider with `TryAddSingleton` so shells/providers can replace them.
- **FR-009**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Administrative pause/unpause endpoints.
- Persisting control-plane state through runtime checkpoints.
- Scheduler enforcement of pause decisions.
- Durable suspension/resume behavior.
- Distributed control-plane provider implementation.

## Acceptance Criteria

- Tests prove store save/find/list/upsert behavior.
- Tests prove default pause decisions allow unmatched targets and block matched holds deterministically.
- Tests prove DI resolves and can replace the default provider.
- Focused runtime and architecture tests pass.
