# Implementation Plan: Runtime Control Plane State Store

**Branch**: `codex/runtime-control-plane-state-store` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first service boundary around existing control-plane pause/unpause contracts. The slice stores administrative `ControlPlaneState` separately from workflow continuation state and provides a default pause-decision provider that evaluates matching active holds at named runtime boundaries.

## Technical Context

- `ControlPlaneState`, `ControlPlaneHold`, `SchedulerPauseDecision`, `RuntimePauseBoundary`, and ingress pause models already exist.
- Runtime API composition does not currently register a control-plane state store or pause-decision provider.
- Pause/unpause are control-plane operations and must remain separate from durable suspend/resume and volatile/internal continue.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Store and provider use Runtime.Core models only. |
| Control-plane state is not continuation state | PASS | Store is separate from checkpoint split-state stores and workflow execution state. |
| Provider neutrality | PASS | Registration uses `TryAddSingleton`; shells/providers can replace the defaults. |
| Scope control | PASS | No API endpoints, scheduler enforcement, distributed provider, or checkpoint persistence. |

## Implementation Steps

1. Add `IControlPlaneStateStore` and in-memory implementation.
2. Add `IRuntimePauseDecisionProvider` and default matching provider.
3. Register both defaults in `WorkflowsRuntimeApiFeature`.
4. Document the extension points.
5. Add focused store, provider, and DI tests.
6. Run validation and self-review.

## Risks

- The provider could be mistaken for full pause behavior. Keep scheduler enforcement and administrative command APIs out of scope.
- Host drain semantics can become nuanced. This slice preserves the hold's continuation policy but does not enforce drain behavior.
