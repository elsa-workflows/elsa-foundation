namespace Elsa.Workflows.Runtime.Core.Middleware;

public sealed class RuntimeWorkflowLoadStateMiddleware : WorkflowRuntimeMiddlewareBase;

public sealed class RuntimeWorkflowSchedulingMiddleware : WorkflowRuntimeMiddlewareBase;

// RuntimeWorkflowCheckpointMiddleware is a real (non-placeholder) middleware — see RuntimeWorkflowCheckpointMiddleware.cs.

public sealed class RuntimeWorkflowPostCommitMiddleware : WorkflowRuntimeMiddlewareBase;
