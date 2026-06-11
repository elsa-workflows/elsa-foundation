# Implementation Plan: Runtime Remove Execution Pool

**Branch**: `codex/runtime-remove-execution-pool` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Remove the unused workflow execution pool contract so runtime execution ownership stays centered on actor-style execution agents.

## Technical Context

- `IWorkflowExecutionPool` has no implementation or registrations.
- `IWorkflowExecutionAgentProvider` is registered by `WorkflowsRuntimeApiFeature` and provides one mailbox/agent per workflow execution ID.
- Existing tests already cover agent provider contract shape and DI registration.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Actor-style execution agents | PASS | Keeps `WorkflowExecutionId -> agent/mailbox` as the ownership seam. |
| Runtime executes pinned artifacts | PASS | Avoids a start API without executable identity. |
| Runtime must not depend on Design | PASS | Removal does not introduce new dependencies. |
| Elsa 3 compatibility bounded | PASS | Does not add live-instance resume compatibility surface. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous PR-loop task complete.
3. Remove `IWorkflowExecutionPool`.
4. Add/adjust tests proving agent provider remains the runtime ownership seam and pool registration is absent.
5. Run focused validation and self-review.

## Risks

- External code may have referenced the unused contract. The contract was not registered or implemented, and the new execution seam is the agent provider.
