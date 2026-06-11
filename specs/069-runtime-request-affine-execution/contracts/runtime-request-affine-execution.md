# Contract: Runtime Request-Affine Execution

## Dispatch Options

`WorkflowExecutionCommandDispatchOptions` carries non-durable dispatch-only values. Its first supported value is an optional ambient `IServiceProvider`.

Rules:

- Options are passed by method call only.
- Options are not copied into `WorkflowExecutionCommandEnvelope`.
- Options are not stored in scheduler work payload, command metadata, envelope metadata, checkpoints, or continuation state.

## Drain Ambient Services

When a scheduler drain request carries ambient services, `WorkflowSchedulerDrainer` exposes them through `IWorkflowExecutionAmbientServicesAccessor` for the duration of that async drain. After the drain completes or faults, the previous ambient value is restored.

## Activity Invocation

`WorkflowInvokeActivitySchedulerWorkHandler` resolves runtime services and constructs `SimpleActivityExecutionContext` from ambient services when present. If no ambient services exist, it creates and disposes an internal scope as before.

## Durable Boundary Rule

Durable resume and background work do not inherit request-affine services. A later command can supply a fresh ambient service provider only if it is executing inside a live request-affine caller.
