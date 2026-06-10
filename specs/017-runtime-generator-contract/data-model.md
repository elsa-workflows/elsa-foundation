# Data Model: Runtime Generator Contract

## `GeneratorRegistration`

- `GeneratorId`: Runtime identity for an active generator registration.
- `WorkflowExecutionId`: Owning workflow execution.
- `GeneratorActivityExecutionId`: Long-lived activity execution owned by the generator activity.
- `OwningScopeActivityExecutionId`: Optional parent/composite/branch scope activity execution boundary.
- `BranchId`: Optional branch identity.
- `Status`: Runtime generator status.
- `StopPolicy`: Generator lifetime rule; defaults to scope-end semantics at call sites.
- `BackpressurePolicy`: Runtime response when generated events cannot be processed safely.
- `RegisteredAt` / `ExpiresAt`: Registration time and optional time-window boundary.
- `Metadata`: Runtime provider/diagnostic metadata.

## `GeneratedEvent`

- `GeneratedEventId`: Identity for diagnostics, ordering, and history projection.
- `WorkflowExecutionId`: Owning workflow execution.
- `GeneratorActivityExecutionId`: Generator activity execution that emitted the event.
- `BranchId`: Optional branch identity.
- `Name`: Emission name understood by runtime/downstream scheduling.
- `Sequence`: Monotonic generator-local ordering value.
- `OccurredAt`: Emission occurrence time.
- `Durability`: Volatile, durable, or policy-controlled emission durability.
- `PayloadValue`: Optional durable value reference for captured payload data.
- `Metadata`: Runtime provider/diagnostic metadata.

## `SchedulerGeneratedEventWorkItem`

- Wraps a `GeneratedEvent` as deterministic scheduler work.
- Derives workflow and generator activity execution identity from the generated event to avoid mismatched identities.
- Carries `WorkItemId`, `EnqueuedAt`, `Reason`, and `Metadata`.

## `SchedulerState`

- Adds `ActiveGenerators`.
- Adds `PendingGeneratedEvents`.
- Keeps generated events separate from:
  - `PendingWork`
  - `PendingCompletionWork`
  - `PendingContinuations`
  - `VolatileWaits`
