# Contract: Runtime Control Plane State Store

## Store Boundary

`IControlPlaneStateStore` stores administrative `ControlPlaneState` outside workflow continuation state:

```text
SaveAsync(ControlPlaneState)
FindAsync(controlPlaneStateId)
ListForWorkflowExecutionAsync(workflowExecutionId)
ListAllAsync()
```

The in-memory default is a single-node provider for current runtime tests. Durable or distributed providers can replace it.

## Pause Decision Boundary

`IRuntimePauseDecisionProvider.DecideAsync(RuntimePauseDecisionRequest)` evaluates whether runtime work can advance at a named `RuntimePauseBoundary`.

The default provider:

- Reads active control-plane holds from `IControlPlaneStateStore`.
- Matches workflow execution, activity execution, generator, ingress source, worker, and host scopes by target ID.
- Ignores non-matching targets.
- Picks the oldest matching hold and then the lowest hold ID for deterministic results.
- Returns `SchedulerPauseDecision` without mutating workflow continuation state.

## Separation Rule

Pause/unpause are administrative control-plane operations. They do not mean durable workflow suspension/resume and they do not create bookmarks or volatile wait continuations.
