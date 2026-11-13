namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Policy-aware preservation of immutable owner inputs; never diagnostic read-model authority.</summary>
public static class RuntimeHistorySourceFacts
{
    /// <summary>Attaches an actual owner's immutable origin before its intent/pending projection is persisted.</summary>
    public static RuntimeCheckpointCommit WithPostCommitCausation(RuntimeCheckpointCommit commit,
        Func<RuntimePostCommitIntent, RuntimeHistoryCausation?> ownerFactory, PersistenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(ownerFactory); ArgumentNullException.ThrowIfNull(scope);
        var changed = false;
        var intents = commit.PostCommitIntents.Select(intent => Stamp(intent, out _)).ToArray();
        var outbox = commit.StateChanges.PostCommitOutbox.Select(change =>
        {
            if (change.Operation != RuntimeStateChangeOperation.Upsert || change.State is not { } item) return change;
            var intent = Stamp(item.Intent, out var owned);
            if (!owned) return change;
            if (item.OutboxItemId != RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, item.Intent.IntentId) ||
                item.Status != RuntimePostCommitOutboxStatus.Pending || item.RecipientSnapshot is not null ||
                item.DeliveryAttemptCount != 0 || item.DeliveryFencingToken != 0 || item.DeliveringOwnerId is not null ||
                item.DeliveryStartedAt is not null || item.DeliveredAt is not null || item.DeliveryVisibleAfter is not null ||
                item.StimulusDeliverySourceScope is { } priorScope && priorScope != scope)
                throw new InvalidOperationException("Source causation must be established before delivery or scope changes.");
            if (ReferenceEquals(intent, item.Intent) && item.StimulusDeliverySourceScope == scope) return change;
            changed = true;
            return change with { State = new RuntimePostCommitOutboxItem(item.OutboxItemId, intent, item.Status, item.RecordedAt,
                item.AvailableAt, item.RetryPolicy, item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt,
                item.DeliveredAt, item.LastFailureMessage, item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter,
                item.StimulusDeliveryProtocolVersion, scope, item.RecipientSnapshot) };
        }).ToArray();
        return changed ? commit with { PostCommitIntents = intents, StateChanges = commit.StateChanges.WithPostCommitOutbox(outbox) } : commit;

        RuntimePostCommitIntent Stamp(RuntimePostCommitIntent intent, out bool owned)
        {
            var cause = ownerFactory(intent); owned = cause is not null;
            if (cause is null) return intent;
            if (intent.WorkflowExecutionId != commit.WorkflowExecutionId || cause.WorkflowExecutionId != commit.WorkflowExecutionId ||
                string.IsNullOrWhiteSpace(cause.HistoryEventId) || cause.DeliveryId != RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId) ||
                new[] { cause.HistoryEventId, cause.WorkflowExecutionId, cause.DeliveryId, cause.CommandId, cause.SchedulerWorkItemId }
                    .Any(value => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024)))
                throw new InvalidOperationException("Source causation must name its bounded originating run, event and exact outbox.");
            if (intent.HistoryCausation is { } prior)
            {
                if (prior != cause) throw new InvalidOperationException("One immutable intent has conflicting source causation.");
                return intent;
            }
            changed = true;
            return new RuntimePostCommitIntent(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt,
                intent.ActivityExecutionId, intent.IdempotencyKey, intent.Payload, intent.Metadata, intent.DependsOnWaitRegistrationId,
                intent.WaitFailurePolicy, intent.MaterializedSchedulerWorkItem, intent.NotBefore) { HistoryCausation = cause };
        }
    }

    public static bool IsFinalProfile(RuntimeHistoryCapturePolicy? policy) => policy is { PolicyId: "runtime-history-v3", Version: 3 };
    public static string EventName(RuntimeHistorySourceFactKind kind) => kind switch
    {
        RuntimeHistorySourceFactKind.IncidentCreated => "IncidentRecorded",
        _ when Enum.IsDefined(kind) => kind.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static IReadOnlyList<RuntimeHistorySourceFact>? ForPolicy(RuntimeHistoryCapturePolicy? policy,
        Func<IEnumerable<RuntimeHistorySourceFact>> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return IsFinalProfile(policy) ? ForPolicy(policy, facts()) : null;
    }
    /// <summary>Preserves absent/older pinned policies rather than introducing source facts into their replay identity.</summary>
    public static IReadOnlyList<RuntimeHistorySourceFact>? ForPolicy(RuntimeHistoryCapturePolicy? policy,
        IEnumerable<RuntimeHistorySourceFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsFinalProfile(policy)) return null;
        // These prove actual owner decisions even when a pinned vocabulary omits a newer event kind.
        // The capture boundary independently filters emitted events by SupportedKinds.
        // A native cancellation/incident decision can legitimately own more facts than one
        // processing window. Keep every fact in its original logical group and order; the
        // window bounds temporary processing, not the width of an admitted native decision.
        var admitted = new List<RuntimeHistorySourceFact>();
        foreach (var window in facts.Chunk(256)) admitted.AddRange(window);
        return admitted.Count == 0 ? null : Array.AsReadOnly(admitted.ToArray());
    }
}
