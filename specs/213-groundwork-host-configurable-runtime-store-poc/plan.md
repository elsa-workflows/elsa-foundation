# Implementation Plan: Groundwork Host-Configurable Runtime Store POC

**Branch**: `sfmskywalker/groundwork-persistence-feasibility` | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)

## Summary

Implement a narrow opt-in Groundwork bridge in Elsa and validate host-owned provider switching for one runtime low-risk store contract (`IWorkflowExecutableStore`). Preserve existing defaults and introduce explicit hot-path viability gates for future migrations.

## Technical Context

- Runtime already exposes replacement contracts and in-memory defaults through `WorkflowsRuntimeApiFeature`.
- Host composition already selects persistence features through shell configuration.
- Groundwork offers provider-neutral manifest + document-store capabilities with provider-specific materializers/adapters.
- This slice proves wiring and contract stability, not runtime hot-path migration.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Provider-neutral invariants independent of provider | PASS | Runtime contract remains provider-neutral; provider chosen in host composition. |
| Provider module decomposition | PASS | Bridge keeps provider adapters outside core/runtime domain contracts. |
| Runtime/store replacement-contract semantics | PASS | Existing replacement contract remains authoritative boundary. |
| Runtime hot-path conservative migration | PASS | Operational stores remain gated and out of scope for this slice. |

## Implementation Steps

1. Add `Elsa.Persistence.Groundwork` bridge project skeleton with options and provider-adapter abstractions.
2. Add runtime storage-driver project with Groundwork-backed `IWorkflowExecutableStore`.
3. Add host composition wiring for opt-in bridge and provider adapter selection.
4. Add tests for provider-neutral contract behavior and fallback/default behavior.
5. Add hot-path viability gate artifact for future migration decisions.
6. Update extension-point docs/maps when new replacement implementations are introduced.

## Risks

- Groundwork provider adapter APIs must remain host-owned; avoid leaking provider dependencies into runtime core.
- Overreaching this slice into hot-path migrations would violate the conservative operational model.
- Test coverage must prove fallback behavior to prevent accidental breakage of current defaults.

