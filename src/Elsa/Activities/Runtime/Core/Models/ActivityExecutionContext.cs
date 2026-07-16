using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// The deliberately small context visible to ordinary activity code. Workflow values arrive through
/// hydrated properties; services arrive through constructor injection.
/// </summary>
public sealed record ActivityExecutionContext
{
    public ActivityExecutionContext(
        string workflowExecutionId,
        string invocationId,
        string attemptId,
        string executableNodeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentNullException.ThrowIfNull(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        WorkflowExecutionId = workflowExecutionId;
        InvocationId = invocationId;
        AttemptId = attemptId;
        ExecutableNodeId = executableNodeId;
        CancellationToken = cancellationToken;
    }

    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public string ExecutableNodeId { get; }
    public CancellationToken CancellationToken { get; }

    internal static ActivityExecutionContext FromLegacy(IActivityExecutionContext context) =>
        new(
            workflowExecutionId: string.Empty,
            invocationId: context.Activity.Id,
            attemptId: string.Empty,
            executableNodeId: context.Activity.NodeId,
            context.CancellationToken);
}

/// <summary>Author-facing base for a transient activity that returns one atomic typed result.</summary>
public abstract class Activity<TResult> : ActivityBase
{
    protected abstract ValueTask<ActivityTransition<TResult>> ExecuteAsync(ActivityExecutionContext context);

    protected sealed override async ValueTask<ActivityTransition> ExecuteTransitionAsync(IActivityExecutionContext context) =>
        await ExecuteAsync(ActivityExecutionContext.FromLegacy(context));
}
