# Implementation Plan: Runtime Pause Boundary Enforcement

**Branch**: `codex/runtime-pause-boundary-enforcement` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the first scheduler enforcement point for control-plane pause decisions. The scheduler drainer peeks at the next queued work item, asks a replaceable pause gate whether it may advance, and leaves the item queued when blocked.

## Technical Context

- `IControlPlaneStateStore` and `IRuntimePauseDecisionProvider` already exist.
- `IWorkflowSchedulerWorkQueue` supports `ListAsync(limit: 1)` and `DequeueAsync`, so enforcement must peek before dequeue to avoid losing paused work.
- `RuntimeSchedulerDrainResult` currently only distinguishes completed and faulted work; pause-blocked work needs a distinct result status.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses Runtime.Core scheduler and control-plane models only. |
| Pause is control-plane, not suspension | PASS | Blocks scheduler advancement without writing durable suspension state. |
| Single-writer scheduler state | PASS | Peeks/dequeues through the existing workflow scheduler queue under the active drain. |
| Scope control | PASS | No admin APIs, ingress adapters, or queue-provider redesign. |

## Implementation Steps

1. Add `IWorkflowSchedulerPauseGate` and default `WorkflowSchedulerPauseGate`.
2. Add pause-blocked drain result status and validation.
3. Teach `WorkflowSchedulerDrainer` to peek and stop before dequeue when blocked.
4. Register the default pause gate in `WorkflowsRuntimeApiFeature`.
5. Document the extension point.
6. Add focused scheduler, generated-event, and DI tests.
7. Run validation and self-review.

## Risks

- Some command kinds do not yet map cleanly to named pause boundaries. This slice gates only activity-start and generated-event boundaries and lets other work proceed unchanged.
- The queue API has no atomic peek/dequeue pair. This remains safe for the current single-writer drain model; distributed providers can replace the queue/drainer together later if they need stronger atomicity.
