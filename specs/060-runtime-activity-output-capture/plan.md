# Implementation Plan: Runtime Activity Output Capture

**Branch**: `codex/runtime-activity-output-capture` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend activity invocation so successful activity outputs are runtime-visible as active-scope values and explicitly declared captures become durable value checkpoint state.

## Technical Context

- `RuntimeInputBinding`, `RuntimeOutputCapture`, `ActiveActivityOutput`, `IRuntimeActivityOutputRegister`, and `DurableValueState` already exist.
- `WorkflowInvokeActivitySchedulerWorkHandler` currently invokes activities with `outputs: null` and completes without publishing output values.
- Durable value checkpoint projection already exists through `RuntimeCheckpointCommitter` and `InMemoryRuntimeCheckpointWriter`.
- `Activities.Runtime` can resolve workflow runtime services from the scheduler work scope.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses executable node output capture declarations only. |
| Activity outputs are scoped/ephemeral | PASS | Publishes to active output register; durable state exists only through declared captures. |
| Checkpoints separate from policy | PASS | Durable captures use a named checkpoint and existing committer. |
| Resume/suspension separation | PASS | Capture runs only after successful completion, not durable bookmark suspension. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark the previous PR-loop task complete.
3. Record output values in `SimpleActivityExecutionContext`.
4. Pass runtime output arguments for executable node output capture declarations.
5. Publish successful outputs to `IRuntimeActivityOutputRegister`.
6. Commit declared durable captures through `DurableValueCaptured` checkpoint.
7. Add focused activity invocation tests.
8. Register default active output register service if missing.
9. Run focused validation and self-review.

## Risks

- Output naming is based on runtime output capture names and activity `Set` calls; richer activity-author helpers remain future work.
- External/custom durable storage remains a declaration boundary until a provider slice implements it.
