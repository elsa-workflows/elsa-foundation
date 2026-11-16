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
        : this(workflowExecutionId, invocationId, attemptId, executableNodeId, cancellationToken, triggerPayload, triggerNodeId, null, null)
    {
    }

    /// <summary>Creates a context that also carries the occurrence's resolved logical operation keys.</summary>
    public ActivityExecutionContext(
        string workflowExecutionId,
        string invocationId,
        string attemptId,
        string executableNodeId,
        CancellationToken cancellationToken,
        JsonElement? triggerPayload,
        string? triggerNodeId,
        string? operationKey,
        string? targetOperationKey)
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
        OperationKey = string.IsNullOrWhiteSpace(operationKey) ? invocationId : operationKey;
        TargetOperationKey = string.IsNullOrWhiteSpace(targetOperationKey) ? null : targetOperationKey;
    }

    public string WorkflowExecutionId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public string ExecutableNodeId { get; }
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// The logical operation key an external participant deduplicates this activity occurrence's effect on. It is
    /// stable across crash re-attempts, boundary retries and operator retries. The runtime resolves it from the key a
    /// structural parent assigned, then the occurrence's first-attempt identity; it defaults to
    /// <see cref="InvocationId"/>.
    /// </summary>
    public string OperationKey { get; }

    /// <summary>
    /// The operation key of the effect this occurrence acts on (for example the forward step a compensation
    /// handler undoes), when a structural parent assigned one; otherwise <see langword="null"/>.
    /// </summary>
    public string? TargetOperationKey { get; }

    internal JsonElement? TriggerPayload { get; }
    internal string? TriggerNodeId { get; }

    /// <summary>
    /// Returns a copy that carries <paramref name="operationKey"/> and <paramref name="targetOperationKey"/>. A blank
    /// operation key falls back to <see cref="InvocationId"/>.
    /// </summary>
    public ActivityExecutionContext WithOperationKeys(string? operationKey, string? targetOperationKey) =>
        new(
            WorkflowExecutionId,
            InvocationId,
            AttemptId,
            ExecutableNodeId,
            CancellationToken,
            TriggerPayload,
            TriggerNodeId,
            operationKey,
            targetOperationKey);
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
