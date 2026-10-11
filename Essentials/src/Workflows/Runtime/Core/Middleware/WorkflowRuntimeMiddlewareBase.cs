using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Middleware;

public abstract class WorkflowRuntimeMiddlewareBase : IWorkflowRuntimeMiddleware
{
    public virtual ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next) => next(context);
}
