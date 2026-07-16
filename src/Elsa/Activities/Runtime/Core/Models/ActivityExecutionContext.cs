using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using System.Text.Json;

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
        : this(workflowExecutionId, invocationId, attemptId, executableNodeId, cancellationToken, null, null)
    {
    }

    internal ActivityExecutionContext(
        string workflowExecutionId,
        string invocationId,
        string attemptId,
        string executableNodeId,
        CancellationToken cancellationToken,
        JsonElement? triggerPayload = null,
        string? triggerNodeId = null)
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
        TriggerPayload = triggerPayload?.Clone();
        TriggerNodeId = string.IsNullOrWhiteSpace(triggerNodeId) ? null : triggerNodeId;
    }

    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public string ExecutableNodeId { get; }
    public CancellationToken CancellationToken { get; }
    internal JsonElement? TriggerPayload { get; }
    internal string? TriggerNodeId { get; }

    internal static ActivityExecutionContext FromLegacy(IActivityExecutionContext context)
    {
        var identity = context as IActivityInvocationIdentity;
        return new(
            workflowExecutionId: identity?.WorkflowExecutionId ?? string.Empty,
            invocationId: identity?.InvocationId ?? context.Activity.Id,
            attemptId: identity?.AttemptId ?? string.Empty,
            executableNodeId: identity?.ExecutableNodeId ?? context.Activity.NodeId,
            context.CancellationToken,
            identity?.TriggerPayload,
            identity?.TriggerNodeId);
    }
}

/// <summary>Identity carrier implemented by the engine's private execution context adapter.</summary>
public interface IActivityInvocationIdentity
{
    string WorkflowExecutionId { get; }
    string InvocationId { get; }
    string AttemptId { get; }
    string ExecutableNodeId { get; }
    JsonElement? TriggerPayload { get; }
    string? TriggerNodeId { get; }
}

/// <summary>Author-facing base for a transient activity that returns one atomic typed result.</summary>
public abstract class Activity<TResult> : ActivityBase, IActivityResult<TResult>
{
    protected abstract ValueTask<ActivityTransition<TResult>> ExecuteAsync(ActivityExecutionContext context);

    protected sealed override async ValueTask<ActivityTransition> ExecuteTransitionAsync(IActivityExecutionContext context) =>
        await ExecuteAsync(ActivityExecutionContext.FromLegacy(context));
}
