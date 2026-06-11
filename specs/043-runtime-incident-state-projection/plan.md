# Implementation Plan: Runtime Incident State Projection

**Branch**: `codex/runtime-incident-state-projection` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend checkpoint-state projection to incident state. Add `IIncidentStateStore`, an in-memory default implementation, and projection from `RuntimeCheckpointCommit.StateChanges.Incidents` under the same serialized checkpoint writer gate used for the previous split-state stores.

## Technical Context

- `IncidentState` already models minimal runtime continuation state.
- `IncidentHistoryProjection` already separates richer diagnostics from continuation state.
- `RuntimeCheckpointCommit.StateChanges.Incidents` already carries typed incident state changes.
- Existing commits use `RuntimeStateChangeOperation.Append` for incident recording; this slice also allows `Upsert` for later terminal-state updates.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core incident contracts only. |
| Runtime state remains split | PASS | Incident state receives its own store boundary. |
| History outside continuation state | PASS | Store only persists `IncidentState`; `IncidentHistoryProjection` remains observability-only. |
| Scope control | PASS | No incident strategy execution, retry, audit store, or recovery scanner. |

## Implementation Steps

1. Add `IIncidentStateStore` and `InMemoryIncidentStateStore`.
2. Register the in-memory incident store in the runtime API feature.
3. Extend the in-memory checkpoint writer to accept an optional incident state store.
4. Validate incident projection operations and identities before recording writes.
5. Project incident appends/upserts under the writer gate.
6. Add focused store, writer, DI, and architecture validation tests.
7. Update active Speckit pointers and run validation.

## Risks

- The in-memory writer is not a durable transaction provider. Durable providers still need to implement atomic commit semantics across split state stores.
- This slice deliberately does not execute incident resolution strategies or persist incident history projections.
