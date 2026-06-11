# Implementation Plan: Runtime Request-Affine Execution

**Branch**: `codex/runtime-request-affine-execution` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a non-durable request-affine execution lane to the in-process agent/scheduler seam. The lane passes an optional ambient service provider through dispatch options into inline scheduler drainage, where Activities Runtime can use it to build activity execution contexts for request-bound activities.

## Technical Context

- `WorkflowExecutionCommandEnvelope` is durable command data and must remain serializable/live-object-free.
- `InProcessWorkflowExecutionAgent` owns the mailbox and calls `IWorkflowExecutionCommandProcessor`.
- `WorkflowSchedulerCommandProcessor` records durable scheduler work and asks `IWorkflowSchedulerDrainPolicy` whether to drain inline.
- `WorkflowSchedulerDrainer` dispatches work handlers on the same async call chain.
- `WorkflowInvokeActivitySchedulerWorkHandler` currently always creates a fresh scope before resolving activity services and constructing `SimpleActivityExecutionContext`.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Actor-style execution agents | PASS | Adds dispatch options to the agent path; mailbox ownership remains unchanged. |
| Runtime executes pinned artifacts | PASS | Does not alter executable artifact loading. |
| Runtime state split | PASS | Request-affine services are not continuation state. |
| Runtime must not depend on Design | PASS | Adds runtime contracts only. |
| Synchronous HTTP capability | PASS | Establishes the required live request-service lane for later HTTP response activities. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous slice PR-loop task complete.
3. Add non-durable command dispatch options.
4. Carry ambient services through command processor drain requests.
5. Add async-flow ambient services accessor around scheduler drainage.
6. Teach Activities Runtime invocation to use ambient services when supplied.
7. Add focused core/activity tests.
8. Validate, self-review, refresh maps if required, PR loop, and merge.

## Risks

- Ambient request services must never be persisted or replayed after durable suspension.
- Activity invocation must not dispose caller-owned request scopes.
- The request-affine lane is in-process only; distributed providers will need their own explicit behavior.
