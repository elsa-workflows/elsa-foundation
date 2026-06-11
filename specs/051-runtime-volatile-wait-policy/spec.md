# Feature Specification: Runtime Volatile Wait Policy

**Feature Branch**: `codex/runtime-volatile-wait-policy`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after volatile wait contracts exist. Add an overridable default volatile wait policy that keeps volatile waits distinct from durable suspension/bookmark resume.

## Scenarios & Tests

1. Given a host supports in-memory continuation, when the default policy evaluates a volatile wait request, then it allows the volatile wait and preserves requested guardrails.
2. Given a host does not support in-memory continuation, when the default policy evaluates a volatile wait request, then it denies the volatile wait with an actionable reason and durable fallback posture.
3. Given runtime API services are composed, when `IRuntimeVolatileWaitPolicy` is resolved, then the default policy is available and replaceable.

## Requirements

- **FR-001**: Runtime.Core MUST provide a default `IRuntimeVolatileWaitPolicy` implementation.
- **FR-002**: The default policy MUST allow volatile waits only when `HostSupportsInMemoryContinuation` is true.
- **FR-003**: Allowed decisions MUST preserve requested host shutdown, cancellation, durable fallback, and requested duration guardrails.
- **FR-004**: Denied decisions MUST include a clear reason and preserve requested guardrails for diagnostics.
- **FR-005**: Runtime API composition MUST register the default policy with `TryAddSingleton` so shells/providers can replace it.
- **FR-006**: The policy MUST NOT introduce durable bookmark IDs, resume target IDs, or C# callback method names.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Timer/event awaiter implementation.
- Durable bookmark fallback execution.
- Volatile wait scheduler execution.
- Host-specific volatile wait strategies.
- Pause/unpause or suspend/resume control-plane behavior.

## Acceptance Criteria

- Tests prove allowed and denied default decisions.
- Tests prove DI resolves and can replace `IRuntimeVolatileWaitPolicy`.
- Tests prove volatile wait policy decisions remain separate from bookmark resume state.
- Focused runtime and architecture tests pass.
