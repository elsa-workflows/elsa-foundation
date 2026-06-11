# Contract: Runtime API Agent Dispatch

This slice introduces a runtime start dispatcher used by Runtime API and future ingress paths.

## Dispatcher

```csharp
public interface IWorkflowExecutionStartDispatcher
{
    ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
        WorkflowExecutionStartDispatchRequest request,
        CancellationToken cancellationToken = default);
}
```

The dispatcher:

- Validates the requested executable artifact exists.
- Pins the exact `WorkflowExecutableIdentity` in a `WorkflowExecutionStartCommandPayload`.
- Activates an agent through `IWorkflowExecutionAgentProvider`.
- Enqueues a `WorkflowExecutionCommandKind.Start` command envelope.
- Returns the agent's command dispatch result without executing activities inline.

## Runtime API

`ExecuteWorkflowRequestHandler` depends on `IWorkflowExecutionStartDispatcher`. It does not depend on `IWorkflowExecutor`.

The execute endpoint returns an agent dispatch view. HTTP response semantics are intentionally minimal in this slice:

- `202 Accepted` for accepted, duplicate, or deferred mailbox dispatch.
- `409 Conflict` for rejected mailbox dispatch.
- `400 Bad Request` for unknown artifact IDs.
