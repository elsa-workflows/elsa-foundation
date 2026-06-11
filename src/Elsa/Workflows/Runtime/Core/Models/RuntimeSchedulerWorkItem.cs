using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeSchedulerWorkItem
{
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
        IReadOnlyDictionary<string, string>? envelopeMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Scheduler work sequence cannot be negative.");

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
}

public sealed class RuntimeSchedulerWorkQuery
{
    public RuntimeSchedulerWorkQuery(string workflowExecutionId, int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Scheduler work query limit must be greater than zero when provided.");

        WorkflowExecutionId = workflowExecutionId;
        Limit = limit;
    }

    public string WorkflowExecutionId { get; }
    public int? Limit { get; }
}
