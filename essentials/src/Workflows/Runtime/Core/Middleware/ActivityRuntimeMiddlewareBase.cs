using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Middleware;

public abstract class ActivityRuntimeMiddlewareBase : IActivityRuntimeMiddleware
{
    public virtual ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next) => next(context);
}
