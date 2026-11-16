namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Policy-aware preservation of immutable owner inputs; never diagnostic read-model authority.</summary>
public static class RuntimeHistorySourceFacts
{
    private const string PublicationAttempt = "StimulusRoutingAttemptObserved";
    private const string PublicationDisposition = "StimulusRoutingDispositionObserved";
    private static readonly HashSet<string> PublicationDispositions = new(StringComparer.Ordinal)
    {
        "routing-source-unavailable-observed", "routing-source-refused-observed", "routing-failure-observed",
        "routing-capacity-shed-observed", "saved-cohort-routing-acknowledged-observed"
    };

    /// <summary>Builds a closed observation of an actual publication-owner invocation; this model grants no native source authority.</summary>
    public static RuntimeHistoryObservation PublicationObservation(RuntimeHistoryCapturePolicy policy, PostCommitDeliveryContext source,
        string receiptId, string name, DateTimeOffset occurredAt, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (!IsPublicationObservation(name)) throw new ArgumentOutOfRangeException(nameof(name), "Publication observations require a publication-owner kind.");
        if (receiptId.Length > 1024 || source.Intent.HistoryCausation is not { } origin ||
            origin.WorkflowExecutionId != source.Intent.WorkflowExecutionId || origin.DeliveryId != source.SourceOutboxItemId ||
            string.IsNullOrWhiteSpace(origin.HistoryEventId) || source.Claim.Item.StimulusDeliverySourceScope != source.Scope)
            throw new InvalidOperationException("A publication observation requires the exact bounded original source.");
        string EventId(string kind) => "source-history:" + RuntimeHistoryIdentity.CreateBinding([source.Intent.WorkflowExecutionId, receiptId, kind]);
        var cause = name == PublicationDisposition ? origin with { HistoryEventId = EventId(PublicationAttempt) } : origin;
        var attempt = "outbox-attempt:" + ((long)source.Claim.Item.DeliveryAttemptCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":fence:" + source.Claim.FencingToken.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var e = new RuntimeHistoryEvent(EventId(name), source.Intent.WorkflowExecutionId, 0, RuntimeHistoryEventCategory.DeliveryObservation,
            name, occurredAt, source.Intent.ActivityExecutionId, metadata: new Dictionary<string, string> { ["history.reasonCode"] = reasonCode })
            { Authority = RuntimeHistoryAuthority.DeliveryObservation, Causation = cause, AttemptId = attempt };
        var observation = new RuntimeHistoryObservation(policy, e);
        RuntimeHistorySafety.ValidateObservation(observation);
        return observation;
    }

    internal static bool IsPublicationObservation(string name) => name is PublicationAttempt or PublicationDisposition;
    internal static void ValidatePublicationObservation(RuntimeHistoryObservation observation)
    {
        var e = observation.Event;
        var cause = e.Causation;
        if (!IsFinalProfile(observation.Policy) ||
            !new[] { "StimulusPublicationCommitted", PublicationAttempt, PublicationDisposition }
                .All(kind => observation.Policy.SupportedKinds.Contains(kind, StringComparer.Ordinal)) ||
            e.Metadata.Count != 1 || !e.Metadata.TryGetValue("history.reasonCode", out var reason) ||
            (e.Name == PublicationAttempt ? reason != "handler-invocation-observed" : !PublicationDispositions.Contains(reason)) ||
            cause is null || string.IsNullOrWhiteSpace(cause.HistoryEventId) || cause.WorkflowExecutionId != e.WorkflowExecutionId ||
            string.IsNullOrWhiteSpace(cause.DeliveryId) || string.IsNullOrWhiteSpace(e.AttemptId) || e.BookmarkId is not null)
            throw new InvalidOperationException("Publication observations require their complete original vocabulary, source and closed reason.");
    }

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
