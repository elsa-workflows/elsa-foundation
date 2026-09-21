using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public delegate ValueTask WorkflowRuntimeMiddlewareDelegate(WorkflowRuntimePipelineContext context);

public interface IWorkflowRuntimeMiddleware
{
    ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next);
}
