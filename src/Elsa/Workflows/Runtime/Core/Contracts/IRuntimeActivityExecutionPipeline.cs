using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Composes the registered activity runtime middleware into a delegate chain and runs it around an inner terminal
/// delegate (the scheduler work handler). With only pass-through placeholder middleware registered, invoking the
/// pipeline is observationally equal to invoking the terminal directly (behavior-preserving).
/// </summary>
public interface IRuntimeActivityExecutionPipeline
{
    /// <summary>The resolved, inspectable ordering of middleware for this pipeline.</summary>
    RuntimePipelinePlan Plan { get; }

    /// <summary>Runs the middleware chain around <paramref name="terminal"/>.</summary>
    ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate terminal);
}
