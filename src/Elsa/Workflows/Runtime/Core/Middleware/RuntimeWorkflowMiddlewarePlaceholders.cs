namespace Elsa.Workflows.Runtime.Core.Middleware;

public sealed class RuntimeWorkflowLoadStateMiddleware : WorkflowRuntimeMiddlewareBase;

public sealed class RuntimeWorkflowSchedulingMiddleware : WorkflowRuntimeMiddlewareBase;

public sealed class RuntimeWorkflowCheckpointMiddleware : WorkflowRuntimeMiddlewareBase;

public sealed class RuntimeWorkflowPostCommitMiddleware : WorkflowRuntimeMiddlewareBase;
