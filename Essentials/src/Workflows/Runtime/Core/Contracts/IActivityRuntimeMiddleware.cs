using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public delegate ValueTask ActivityRuntimeMiddlewareDelegate(ActivityRuntimePipelineContext context);

public interface IActivityRuntimeMiddleware
{
    ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next);
}
