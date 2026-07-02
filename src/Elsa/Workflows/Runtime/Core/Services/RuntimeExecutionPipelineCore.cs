using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Shared runtime-execution-pipeline mechanics for the workflow and activity pipelines. Both pipeline kinds resolve
/// their middleware eagerly in the same way and fold them identically; this keeps that logic in one place so the two
/// public pipelines are thin, type-specific wrappers over the same behavior.
/// </summary>
internal static class RuntimeExecutionPipelineCore
{
    /// <summary>
    /// Validates the plan/service provider, asserts the plan matches <paramref name="expectedKind"/>, and eagerly
    /// resolves each plan step's middleware instance via <paramref name="serviceProvider"/> in plan order.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware contract for this pipeline kind.</typeparam>
    /// <param name="plan">The pipeline plan whose steps are resolved.</param>
    /// <param name="serviceProvider">Resolves each step's middleware instance.</param>
    /// <param name="expectedKind">The pipeline kind the plan must declare.</param>
    /// <param name="planDescription">Human-readable plan name used in the wrong-kind error message (e.g. "a workflow", "an activity").</param>
    public static IReadOnlyList<TMiddleware> ResolveMiddleware<TMiddleware>(
        RuntimePipelinePlan plan,
        IServiceProvider serviceProvider,
        RuntimePipelineKind expectedKind,
        string planDescription)
        where TMiddleware : notnull
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (plan.PipelineKind != expectedKind)
            throw new ArgumentException($"Expected {planDescription} runtime pipeline plan but received '{plan.PipelineKind}'.", nameof(plan));

        return plan.Steps
            .Select(step => (TMiddleware)serviceProvider.GetRequiredService(step.MiddlewareType))
            .ToArray();
    }

    /// <summary>
    /// Folds the ordered middleware right-to-left so the first plan step is the outermost frame and
    /// <paramref name="terminal"/> (the scheduler work handler) is the innermost, then invokes the chain. With only
    /// pass-through placeholders this reduces to <c>terminal(context)</c>. Callers guard <c>context</c> and
    /// <c>terminal</c> for null before invoking so argument names in thrown exceptions match the public signature.
    /// </summary>
    /// <remarks>
    /// Generic over the pipeline's named delegate <typeparamref name="TDelegate"/> so the chain is built from and
    /// invoked as those delegates directly — no per-dispatch <c>Func</c>-adapter closures. <paramref name="wrap"/> and
    /// <paramref name="invoke"/> are passed as static (non-capturing) lambdas by the callers, so the only per-dispatch
    /// allocations are the middleware frames themselves.
    /// </remarks>
    public static ValueTask Invoke<TMiddleware, TContext, TDelegate>(
        IReadOnlyList<TMiddleware> middleware,
        TContext context,
        TDelegate terminal,
        Func<TMiddleware, TDelegate, TDelegate> wrap,
        Func<TDelegate, TContext, ValueTask> invoke)
        where TDelegate : Delegate
    {
        var next = terminal;
        for (var index = middleware.Count - 1; index >= 0; index--)
            next = wrap(middleware[index], next);

        return invoke(next, context);
    }
}
