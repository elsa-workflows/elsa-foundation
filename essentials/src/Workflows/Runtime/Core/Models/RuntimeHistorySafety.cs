namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Shared validation for history participants before a provider may commit them.</summary>
public static class RuntimeHistorySafety
{
    public static bool IsSafeMetadataKey(string key) => SafeMetadata.Contains(key);

    /// <summary>Validates an unpositioned delivery-owner observation, independently of a runtime checkpoint.</summary>
    public static void ValidateObservation(RuntimeHistoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(observation.Policy);
        ArgumentNullException.ThrowIfNull(observation.Event);
        var e = observation.Event;
        ValidateMetadata(e);
        var timer = DurableTimerHistoryFacts.IsTimerObservation(e.Name);
        if (e.Authority != RuntimeHistoryAuthority.DeliveryObservation ||
            e.Category != (timer ? RuntimeHistoryEventCategory.SchedulerLifecycle : RuntimeHistoryEventCategory.DeliveryObservation) ||
            e.SchemaVersion != 1 || e.Sequence != 0 || e.PhysicalCommitId is not null || e.RecordedAt is not null ||
            e.CheckpointId is not null || e.CheckpointOrdinal is not null ||
            !(DeliveryObservationKinds.Contains(e.Name) || RuntimeHistorySourceFacts.IsPublicationObservation(e.Name) || timer) ||
            !observation.Policy.SupportedKinds.Contains(e.Name, StringComparer.Ordinal))
            throw new InvalidOperationException("A delivery observation must be safe, unpositioned and admitted by its retained policy.");
        if (RuntimeHistorySourceFacts.IsPublicationObservation(e.Name)) RuntimeHistorySourceFacts.ValidatePublicationObservation(observation);
        if (timer) DurableTimerHistoryFacts.ValidateObservation(e);
    }

    public static void Validate(RuntimeHistoryBatch batch, string workflowExecutionId)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (batch.SourceFingerprint is { } source && (source.Length != 64 || source.Any(character => !char.IsAsciiHexDigit(character))))
            throw new InvalidOperationException("A history source proof must be a bounded semantic fingerprint.");
        // Native decisions retain one logical group, regardless of their resource width.
        // Validate every candidate in bounded temporary windows, without resetting ordinals
        // or introducing another source identity, allocation, transaction or checkpoint.
        foreach (var window in batch.Candidates.Chunk(128))
        foreach (var candidate in window)
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
                    mutation.Reason?.Length > 128 || mutation.SelectedBookmarkId?.Length > 1024 || mutation.RelatedActivityExecutionId?.Length > 1024)
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

    // Additional semantic owners extend this vocabulary with their own source-bound validation and proof.
    // Declaring a kind here neither registers an owner nor enables capture for an admitted run.
    private static readonly HashSet<string> DeliveryObservationKinds = new(StringComparer.Ordinal)
    {
        "DeliveryReceived", "DeliveryDispatched", "DeliveryDuplicate", "DeliveryUnmatched", "DeliveryRejected", "DeliveryDeferred", "DeliveryFaulted"
    };

    private static readonly HashSet<string> SafeMetadata = new(StringComparer.Ordinal)
    {
        "history.status", "history.reasonCode", "history.outcomeNames", "history.stimulusType", "history.resumeTargetId",
        "history.outcomeCaptureState", "history.outcomeCount", "history.outcomeContentHash",
        "history.selectedBookmarkId", "history.childWorkflowId", "history.relatedWorkflowExecutionId", "history.timerId", "history.scheduledAt", "history.controlKind",
        "history.valueEvidenceIds", "history.incidentFailureType", "history.originalCheckpoint", "history.capturePolicyId"
        , "history.relatedActivityExecutionId", "history.compensationEntryId", "history.valuePhase", "history.valueEvaluationId", "history.durableStateAddress", "history.operation", "history.lifecycle", "history.storage", "history.sourceCapturedAt"
    };
}
