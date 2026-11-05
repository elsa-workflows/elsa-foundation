namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Shared validation for history participants before a provider may commit them.</summary>
public static class RuntimeHistorySafety
{
    public static void Validate(RuntimeHistoryBatch batch, string workflowExecutionId)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (batch.Candidates.Count > 4096) throw new InvalidOperationException("A logical history group exceeds its bounded candidate limit.");
        if (batch.SourceFingerprint is { } source && (source.Length != 64 || source.Any(character => !char.IsAsciiHexDigit(character))))
            throw new InvalidOperationException("A history source proof must be a bounded semantic fingerprint.");
        foreach (var candidate in batch.Candidates)
        {
            var e = candidate.Event;
            if (e.WorkflowExecutionId != workflowExecutionId || e.CheckpointId != batch.LogicalCheckpointId ||
                e.Authority != RuntimeHistoryAuthority.CommittedTransition || e.SchemaVersion != 1 ||
                !batch.Policy.SupportedKinds.Contains(e.Name, StringComparer.Ordinal))
                throw new InvalidOperationException("History candidate does not agree with its pinned policy, owner or checkpoint.");
            if (e.CheckpointOrdinal is { } ordinal && ordinal != candidate.CheckpointOrdinal)
                throw new InvalidOperationException("A history event cannot disagree with its logical checkpoint ordinal.");
            if (candidate.BookmarkMutation is { } mutation)
            {
                if (e.Category != RuntimeHistoryEventCategory.BookmarkLifecycle || !Enum.IsDefined(mutation.Status) ||
                    string.IsNullOrWhiteSpace(mutation.Bookmark.StimulusType) || mutation.Bookmark.StimulusType.Length > 1024 ||
                    mutation.Bookmark.StimulusHash is null || mutation.Bookmark.StimulusHash.Length > 1024 ||
                    mutation.Reason?.Length > 128 || mutation.SelectedBookmarkId?.Length > 1024)
                    throw new InvalidOperationException("Bookmark history has an invalid typed lifecycle projection.");
            }
            ValidateMetadata(e);
        }
    }

    public static void ValidateMetadata(RuntimeHistoryEvent e)
    {
        if (e.HistoryEventId.Length > 256 || e.WorkflowExecutionId.Length > 1024 || e.Name.Length > 230 ||
            e.Payload is not null || !Enum.IsDefined(e.Category) || !Enum.IsDefined(e.Authority) || e.Metadata.Count > 16 ||
            e.CheckpointOrdinal < 0 || new[] { e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId, e.BookmarkId,
                e.AttemptId, e.ParentActivityExecutionId, e.ExecutionScopeId, e.IterationId, e.Causation?.HistoryEventId,
                e.Causation?.WorkflowExecutionId, e.Causation?.DeliveryId, e.Causation?.CommandId, e.Causation?.SchedulerWorkItemId }
                .Any(value => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024)))
            throw new InvalidOperationException("History candidate has an invalid or unsafe envelope.");
        foreach (var pair in e.Metadata)
            if (!SafeMetadata.Contains(pair.Key) || pair.Value.Length > 2048)
                throw new InvalidOperationException("History metadata is not in the bounded semantic allowlist.");
    }

    private static readonly HashSet<string> SafeMetadata = new(StringComparer.Ordinal)
    {
        "history.status", "history.reasonCode", "history.outcomeNames", "history.stimulusType", "history.resumeTargetId",
        "history.selectedBookmarkId", "history.childWorkflowId", "history.timerId", "history.scheduledAt", "history.controlKind",
        "history.valueEvidenceIds", "history.incidentFailureType", "history.originalCheckpoint", "history.capturePolicyId"
    };
}
