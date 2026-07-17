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

    public ActivityExecutionContext(
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

}

/// <summary>Author-facing base for a transient activity that returns one atomic typed result.</summary>
public abstract class Activity<TResult> : IActivity, IActivityResult<TResult>
{
    protected abstract ValueTask<ActivityTransition<TResult>> ExecuteAsync(ActivityExecutionContext context);

    async ValueTask<ActivityTransition> IActivity.ExecuteAsync(ActivityExecutionContext context) =>
        await ExecuteAsync(context);
}

/// <summary>Author-facing base for a transient activity whose atomic result is <see cref="ActivityUnit"/>.</summary>
public abstract class Activity : Activity<ActivityUnit>;
