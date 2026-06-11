# Implementation Plan: Runtime Generator Emission Scheduler

**Branch**: `codex/runtime-generator-emission-scheduler` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the scheduler-facing seam for in-workflow generator emissions. The slice keeps generator registrations and generated-event lanes inside scheduler state, while providing an overridable default that converts one `GeneratedEvent` into deterministic queued scheduler work.

## Technical Context

- `GeneratorRegistration`, `GeneratedEvent`, and `SchedulerGeneratedEventWorkItem` already exist as Runtime.Core models.
- `SchedulerState` already carries active generator registrations and pending generated-event work as dedicated scheduler lanes.
- `IWorkflowSchedulerWorkQueue` already owns per-workflow scheduler work ordering and idempotency by work item ID.
- A separate generator state store would conflict with the locked split-state model, so this slice stays at the scheduler enqueue boundary.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Scheduler uses Runtime.Core models only. |
| Generator emissions are scheduler work | PASS | No trigger infrastructure or generator-state bucket is introduced. |
| Provider neutrality | PASS | Registration uses `TryAddSingleton`; shells/providers can replace the default. |
| Scope control | PASS | No generator execution loop, pause enforcement, durable replay, or backpressure behavior. |

## Implementation Steps

1. Add `IRuntimeGeneratorEmissionScheduler` and schedule request/result models.
2. Add `GeneratedEvent` command kind for scheduler work classification.
3. Implement `RuntimeGeneratorEmissionScheduler` over `IWorkflowSchedulerWorkQueue`.
4. Register the scheduler in `WorkflowsRuntimeApiFeature`.
5. Document the extension point.
6. Add focused scheduler and DI tests.
7. Run validation and self-review.

## Risks

- The seam could be mistaken for full generator behavior. Keep execution, pause enforcement, and replay explicitly out of scope.
- Deterministic IDs need to stay stable for idempotency; derive them from workflow execution and generated event identity rather than process-local generated IDs.
