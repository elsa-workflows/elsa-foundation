using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Runs the activity runtime middleware (in <see cref="RuntimePipelinePlan"/> order) around a terminal delegate.
/// </summary>
public sealed class RuntimeActivityExecutionPipeline : IRuntimeActivityExecutionPipeline
{
    private readonly IReadOnlyList<IActivityRuntimeMiddleware> _middleware;

    public RuntimeActivityExecutionPipeline(RuntimePipelinePlan plan, IServiceProvider serviceProvider)
    {
        _middleware = RuntimeExecutionPipelineCore.ResolveMiddleware<IActivityRuntimeMiddleware>(
            plan, serviceProvider, RuntimePipelineKind.Activity, "an activity");
        Plan = plan;
    }

    public RuntimePipelinePlan Plan { get; }

    public ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate terminal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terminal);

        return RuntimeExecutionPipelineCore.Invoke(
            _middleware,
            context,
            terminal,
            static (middleware, next) => (ActivityRuntimePipelineContext ctx) => middleware.InvokeAsync(ctx, next),
            static (next, ctx) => next(ctx));
    }
}
