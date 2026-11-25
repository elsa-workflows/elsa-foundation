using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Provider-neutral semantic replay identity, excluding provider-assigned positions.</summary>
public static class RuntimeHistoryIdentity
{
    // Opaque identity code units retain their existing lossless framing; carried JSON is
    // canonical JSON. All stages share the sole hash profile and freeze at first stable.
    private static string Hash<T>(T value) => JsonHashInput.Hash(RuntimeJsonHashProjection.Project(value));
    public static string CreateId(string logicalCheckpointId, string name, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalCheckpointId); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(subject);
        return "history:" + Hash(new { logicalCheckpointId, name, subject });
    }

    /// <summary>Hashes ordered nullable query/access fields without replacing opaque UTF-16 code units.</summary>
    public static string CreateBinding(IReadOnlyList<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Hash(fields);
    }

    public static string Compute(RuntimeHistoryEvent e, int? checkpointOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var hash = Hash(new
        {
            e.HistoryEventId, e.WorkflowExecutionId, e.Category, e.Name, e.OccurredAt,
            e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId,
            CheckpointOrdinal = checkpointOrdinal ?? e.CheckpointOrdinal,
            e.SchemaVersion, e.Authority, e.BookmarkId, e.AttemptId, e.ParentActivityExecutionId,
            e.ExecutionScopeId, e.IterationId, e.Causation, HasPayload = e.Payload.HasValue, e.Payload,
            Metadata = e.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
        });
        if (e.ActivityAncestryKnown is { } known)
            hash = Hash(new { AncestryVersion = 1, Event = hash, Known = known });
        if (e.OccurredAtKnown is { } timeKnown)
            hash = Hash(new { OccurrenceVersion = 1, Event = hash, Known = timeKnown });
        return hash;
    }

    public static string Compute(RuntimeHistoryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Hash(new
        {
            Event = Compute(candidate.Event, candidate.CheckpointOrdinal),
            candidate.BookmarkMutation
        });
    }

    public static RuntimeHistoryEvent Position(RuntimeHistoryEvent e, long sequence, string physicalCommitId, DateTimeOffset recordedAt, int? checkpointOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(e); ArgumentException.ThrowIfNullOrWhiteSpace(physicalCommitId);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        return new(e.HistoryEventId, e.WorkflowExecutionId, sequence, e.Category, e.Name, e.OccurredAt,
            e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId, e.Payload, e.Metadata)
        {
            CheckpointOrdinal = checkpointOrdinal ?? e.CheckpointOrdinal, OccurredAtKnown = e.OccurredAtKnown,
            SchemaVersion = e.SchemaVersion, Authority = e.Authority, BookmarkId = e.BookmarkId, AttemptId = e.AttemptId,
            ParentActivityExecutionId = e.ParentActivityExecutionId, ActivityAncestryKnown = e.ActivityAncestryKnown, ExecutionScopeId = e.ExecutionScopeId,
            IterationId = e.IterationId, Causation = e.Causation, PhysicalCommitId = physicalCommitId, RecordedAt = recordedAt
        };
    }
}
