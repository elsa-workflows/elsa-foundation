using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeSchedulerWorkItem
{
    [JsonConstructor]
    public RuntimeSchedulerWorkItem(
        string workItemId,
        string workflowExecutionId,
        string commandId,
        WorkflowExecutionCommandKind commandKind,
        string envelopeId,
        string idempotencyKey,
        DateTimeOffset enqueuedAt,
        DateTimeOffset recordedAt,
        long? sequence = null,
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? commandMetadata = null,
        IReadOnlyDictionary<string, string>? envelopeMetadata = null,
        string? executionScopeId = null,
        ActivityExecutionAttemptLineage? attempt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Scheduler work sequence cannot be negative.");
        if (executionScopeId is not null && string.IsNullOrWhiteSpace(executionScopeId))
            throw new ArgumentException("Execution scope ID cannot be blank when provided.", nameof(executionScopeId));

        WorkItemId = workItemId;
        WorkflowExecutionId = workflowExecutionId;
        CommandId = commandId;
        CommandKind = commandKind;
        EnvelopeId = envelopeId;
        IdempotencyKey = idempotencyKey;
        EnqueuedAt = enqueuedAt;
        RecordedAt = recordedAt;
        Sequence = sequence;
        Payload = payload?.Clone();
        CommandMetadata = RuntimeModelMetadata.Snapshot(commandMetadata);
        EnvelopeMetadata = RuntimeModelMetadata.Snapshot(envelopeMetadata);
        ExecutionScopeId = executionScopeId;
        Attempt = attempt;
    }

    public string WorkItemId { get; }
    public string WorkflowExecutionId { get; }
    public string CommandId { get; }
    public WorkflowExecutionCommandKind CommandKind { get; }
    public string EnvelopeId { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public DateTimeOffset RecordedAt { get; }
    public long? Sequence { get; }
    public JsonElement? Payload { get; }
    public IReadOnlyDictionary<string, string> CommandMetadata { get; }
    public IReadOnlyDictionary<string, string> EnvelopeMetadata { get; }
    public string? ExecutionScopeId { get; }
    public ActivityExecutionAttemptLineage? Attempt { get; }
}

/// <summary>
/// One finite, forward-only page of scheduler work for a workflow execution.
/// </summary>
public sealed record RuntimeSchedulerWorkQuery : RuntimeStorePageRequest
{
    public RuntimeSchedulerWorkQuery(
        string workflowExecutionId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        WorkflowExecutionId = workflowExecutionId;
    }

    public string WorkflowExecutionId { get; }
}

/// <summary>
/// One bounded page of claimable-backlog discovery; see
/// <see cref="Contracts.IWorkflowSchedulerWorkQueue.ListClaimableWorkflowExecutionIdsAsync"/>.
/// </summary>
public sealed class RuntimeSchedulerClaimableBacklogQuery
{
    public RuntimeSchedulerClaimableBacklogQuery(
        DateTimeOffset now,
        int limit = RuntimeStorePageRequest.DefaultLimit,
        string? afterWorkflowExecutionId = null)
    {
        if (afterWorkflowExecutionId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(afterWorkflowExecutionId);

        Now = now;
        Limit = RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        AfterWorkflowExecutionId = afterWorkflowExecutionId;
    }

    /// <summary>The instant claimability is judged at, as <see cref="RuntimeSchedulerWorkClaimRequest.Now"/> would be.</summary>
    public DateTimeOffset Now { get; }

    public int Limit { get; }

    /// <summary>Exclusive ordinal lower bound, or <see langword="null"/> to start from the first execution.</summary>
    public string? AfterWorkflowExecutionId { get; }
}
