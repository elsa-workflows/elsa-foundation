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
        string? targetOperationKey,
        string? logicalInvocationId = null)
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
        LogicalInvocationId = string.IsNullOrWhiteSpace(logicalInvocationId) ? invocationId : logicalInvocationId;
    }

    public string WorkflowExecutionId { get; }

    /// <summary>
    /// This activity execution's row id. An operator retry or re-execution runs on a new row, so this changes then; use
    /// <see cref="LogicalInvocationId"/> for the identity a retry keeps.
    /// </summary>
    public string InvocationId { get; }

    /// <summary>
    /// The logical invocation identity (ADR 0092 decision 8, amended 2026-11-16): stable across crash re-attempts and
    /// operator retries, new for a re-execution or reschedule. An activity that deduplicates an external effect on one
    /// identity per invocation keys it on this (or on <see cref="OperationKey"/>). It defaults to
    /// <see cref="InvocationId"/>; the runtime sets it from the execution's attempt lineage.
    /// </summary>
    public string LogicalInvocationId { get; }
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
    public ActivityExecutionContext WithOperationKeys(string? operationKey, string? targetOperationKey, string? logicalInvocationId = null) =>
        new(
            WorkflowExecutionId,
            InvocationId,
            AttemptId,
            ExecutableNodeId,
            CancellationToken,
            TriggerPayload,
            TriggerNodeId,
            operationKey,
            targetOperationKey,
            logicalInvocationId ?? LogicalInvocationId);
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
