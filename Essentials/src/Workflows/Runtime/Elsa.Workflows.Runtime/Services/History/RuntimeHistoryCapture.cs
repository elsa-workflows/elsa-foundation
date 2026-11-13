using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Captures immutable semantic candidates before coalescing; it never writes a store.</summary>
internal sealed class RuntimeHistoryCapture(IWorkflowExecutionStateStore executions,
    IEnumerable<RuntimeHistorySourceSupport> sourceSupport, RuntimeHistoryCaptureCapabilities sourceAvailability)
    : IRuntimeCheckpointCommitEnricher, IRuntimeHistoryCaptureSupport
{
    internal static readonly string[] Kinds =
    [
        "WorkflowStarted", "WorkflowCompleted", "WorkflowFaulted", "WorkflowCancelled", "WorkflowSuspended",
        "ActivityScheduled", "ActivityStarted", "ActivityAttemptClaimed", "ActivityResumeClaimed", "ActivityResumeRetried",
        "ActivityCompleted", "ActivitySuspended", "ActivityCancelled", "ActivityRecovered", "ActivityFaulted",
        "BookmarkCreated", "BookmarkConsumed", "BookmarkRetired", "BookmarkRemoved", "BookmarkStaleRemoved",
        "IncidentRecorded", "IncidentResolved", "IncidentResolutionBatchApplied", "WorkflowDispatchChanged", "ValueCaptured", "RuntimeAlterationJob"
    ];

    internal static readonly string[] Version2Kinds = ["BookmarkSuperseded", "DurableStateRecorded", "DurableStateRemoved"];

    public int Order => 2000;

    public bool CanCapture(RuntimeHistoryCapturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!RuntimeHistorySourceFacts.IsFinalProfile(policy) || !sourceAvailability.SourceInputs()) return false;
        if (policy.SupportedKinds.Contains("ValueCaptured", StringComparer.Ordinal) && !sourceAvailability.Values()) return false;
        var available = Kinds.Concat(Version2Kinds).ToHashSet(StringComparer.Ordinal);
        // Post-commit, delivery and timer owners are qualified in their later source/writer slices.
        available.UnionWith(sourceSupport.Where(owner => owner.SourceIntentKind is null).SelectMany(owner => owner.Kinds).Select(RuntimeHistorySourceFacts.EventName));
        return policy.SupportedKinds.All(available.Contains);
    }

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();
        if (commit.HistoryBatches is { Count: > 0 } || IsFlush(commit)) return commit;
        var policy = await PolicyAsync(commit, cancellationToken);
        if (policy is null) return commit.HistorySourceFacts is null ? commit : commit with { HistorySourceFacts = null };
        if (!CanCapture(policy))
        {
            if (policy.Required) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The current runtime cannot capture the retained history profile.");
            return commit with { HistorySourceFacts = null };
        }
        var batch = Build(commit, policy);
        return commit with { HistoryBatches = [batch] };
    }

    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();
        if (commit.HistoryBatches is not { Count: > 0 } batches)
        {
            if (!IsFlush(commit) && (await PolicyAsync(commit, cancellationToken))?.Required == true)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The required logical history contribution is missing.");
            return;
        }
        foreach (var batch in batches) RuntimeHistorySafety.Validate(batch, commit.WorkflowExecutionId);
        if (IsFlush(commit)) return;
        if (batches.Count != 1 || batches[0].SourceFingerprint != SourceFingerprint(commit))
            throw new InvalidOperationException("The logical checkpoint source changed after semantic history capture.");
        var policy = await PolicyAsync(commit, cancellationToken);
        if (policy is null || batches.Count != 1 || !policy.IsEquivalentTo(batches[0].Policy))
            throw new InvalidOperationException("History must retain the actual admitted execution's capture policy.");
        if (!CanCapture(policy))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The retained history capture chain is unavailable.");
        var expected = Build(commit, policy);
        if (!expected.Candidates.Select(RuntimeHistoryIdentity.Compute)
                .SequenceEqual(batches[0].Candidates.Select(RuntimeHistoryIdentity.Compute), StringComparer.Ordinal))
            throw new InvalidOperationException("The final checkpoint no longer agrees with its semantic history candidates.");
    }

    private async ValueTask<RuntimeHistoryCapturePolicy?> PolicyAsync(RuntimeCheckpointCommit commit, CancellationToken token)
    {
        var admitted = await ReadAsync(commit.WorkflowExecutionId, () => executions.FindAsync(commit.WorkflowExecutionId, token), token);
        if (admitted is not null) return admitted.HistoryCapturePolicy;
        if (commit.StateChanges.WorkflowExecution is { } change) return change.State.HistoryCapturePolicy;
        return null;
    }

    private static async ValueTask<T> ReadAsync<T>(string workflowId, Func<ValueTask<T>> read, CancellationToken token)
    {
        try { return await read(); }
        catch (Exception exception) when (RuntimeHistoryReadFailures.IsMalformed(exception))
        { throw new RuntimeHistoryIntegrityException(workflowId, "the capture owner could not interpret its admitted policy", exception); }
        catch (Exception exception) when (!token.IsCancellationRequested && RuntimeHistoryReadFailures.IsUnavailable(exception))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "History capture dependency is unavailable.", exception); }
    }

    private static bool IsFlush(RuntimeCheckpointCommit commit) => commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);

    private static RuntimeHistoryBatch Build(RuntimeCheckpointCommit commit, RuntimeHistoryCapturePolicy policy)
    {
        var result = new List<RuntimeHistoryCandidate>();
        var checkpoint = commit.Checkpoint;
        if (checkpoint.HistoryStartsAtAdmission == true && (policy.Version < 2 || checkpoint.Name != RuntimeCheckpointNames.WorkflowFaulted ||
            commit.StateChanges.WorkflowExecution?.State is not { Status: WorkflowExecutionStatus.Faulted, HistoryCaptureEnabledAtAdmission: true }))
            throw new InvalidOperationException("Initial fault capture requires the actual readable admitted start boundary.");
        var activities = commit.StateChanges.ActivityExecutions.ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var sourceFacts = commit.HistorySourceFacts ?? [];
        var incidents = commit.StateChanges.Incidents.ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var incidentFactIds = sourceFacts.Where(fact => IsIncidentFact(fact.Kind)).Select(fact => fact.IncidentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var suspendedActivities = sourceFacts.Where(fact => fact.Kind == RuntimeHistorySourceFactKind.ActivityContinuationSuspended)
            .Select(fact => fact.ActivityExecutionId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var deletesBookmarks = commit.StateChanges.Bookmarks.Any(change => change.Operation == RuntimeStateChangeOperation.Delete);
        var selectedBookmark = Read(RuntimeMetadataKeys.BookmarkId);
        var selectedActivity = Read(RuntimeMetadataKeys.ActivityExecutionId);
        var cause = checkpoint.HistoryCausation ?? new RuntimeHistoryCausation(CommandId: Read(RuntimeMetadataKeys.CommandId), SchedulerWorkItemId: Read(RuntimeMetadataKeys.SchedulerWorkItemId));

        var orderedContinuation = RuntimeHistorySourceFacts.IsFinalProfile(policy);
        if (orderedContinuation)
        {
            EmitValues(false);
            EmitFacts(true);
            EmitValues(true);
            EmitFacts(false);
        }
        else if (policy.Version >= 2) EmitFacts(null);

        if (commit.StateChanges.WorkflowExecution is { } workflow &&
            (checkpoint.Name is RuntimeCheckpointNames.WorkflowStarted or RuntimeCheckpointNames.WorkflowCompleted or RuntimeCheckpointNames.WorkflowFaulted or RuntimeCheckpointNames.WorkflowCancelled or RuntimeCheckpointNames.WorkflowSuspended ||
             workflow.State.Status == WorkflowExecutionStatus.Cancelled && (commit.HistorySourceFacts ?? []).Any(fact => fact.Kind == RuntimeHistorySourceFactKind.ControlApplied && fact.ReasonCode == "workflow-cancelled")))
            Add(workflow.State.Status == WorkflowExecutionStatus.Cancelled ? "WorkflowCancelled" : checkpoint.Name, RuntimeHistoryEventCategory.WorkflowLifecycle, commit.WorkflowExecutionId,
                metadata: new Dictionary<string, string> { ["history.status"] = workflow.State.Status.ToString() });

        foreach (var change in commit.StateChanges.ActivityExecutions.OrderBy(change => change.State.ExecutionSequence).ThenBy(change => change.StateId, StringComparer.Ordinal))
        {
            var a = change.State;
            if (checkpoint.Name is not (RuntimeCheckpointNames.ActivityScheduled or RuntimeCheckpointNames.ActivityStarted or
                RuntimeCheckpointNames.ActivityAttemptClaimed or RuntimeCheckpointNames.ActivityCompleted or RuntimeCheckpointNames.IntrinsicCompleted or
                RuntimeCheckpointNames.ActivitySuspended or RuntimeCheckpointNames.ActivityCancelled or RuntimeCheckpointNames.ActivityRecovered or RuntimeCheckpointNames.IncidentRecorded) &&
                !(checkpoint.Name == RuntimeCheckpointNames.BookmarkCreated && a.Status == ActivityExecutionStatus.Suspended &&
                  suspendedActivities.Contains(a.InvocationId)))
                continue; // Re-projecting a state is not itself an activity lifecycle transition.
            string? name = a.Status switch
            {
                ActivityExecutionStatus.Scheduled => "ActivityScheduled",
                ActivityExecutionStatus.Completed => "ActivityCompleted",
                ActivityExecutionStatus.Suspended => "ActivitySuspended",
                ActivityExecutionStatus.Cancelled => "ActivityCancelled",
                ActivityExecutionStatus.Recovered => "ActivityRecovered",
                ActivityExecutionStatus.Faulted => "ActivityFaulted",
                _ => checkpoint.Name == RuntimeCheckpointNames.ActivityStarted ? "ActivityStarted" :
                    checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed ?
                        (Read(RuntimeMetadataKeys.ResumeTargetId) is not null ?
                            (deletesBookmarks ? "ActivityResumeClaimed" : "ActivityResumeRetried") : "ActivityAttemptClaimed") : null
            };
            if (name is null) continue;
            Add(name, RuntimeHistoryEventCategory.ActivityLifecycle, change.StateId, a,
                metadata: new Dictionary<string, string> { ["history.status"] = a.Status.ToString() });
        }

        foreach (var change in commit.StateChanges.Bookmarks.OrderBy(change => change.StateId, StringComparer.Ordinal))
        {
            var bookmark = change.State;
            activities.TryGetValue(bookmark.ActivityExecutionId, out var activity);
            var deleted = change.Operation == RuntimeStateChangeOperation.Delete;
            var stale = deleted && checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed && activity?.Status == ActivityExecutionStatus.Completed;
            var initialClaim = deleted && checkpoint.Name == RuntimeCheckpointNames.ActivityAttemptClaimed;
            var selected = StringComparer.Ordinal.Equals(selectedBookmark, bookmark.BookmarkId);
            var status = !deleted ? RuntimeHistoryBookmarkStatus.Active : initialClaim && selected ? RuntimeHistoryBookmarkStatus.Consumed : RuntimeHistoryBookmarkStatus.Removed;
            var name = !deleted ? "BookmarkCreated" : stale ? "BookmarkStaleRemoved" : initialClaim ? selected ? "BookmarkConsumed" : "BookmarkRetired" : "BookmarkRemoved";
            var reason = !deleted ? null : stale ? "stale-terminal-cleanup" : initialClaim ? selected ? "selected-resume" : "sibling-resume-won" : checkpoint.Name == RuntimeCheckpointNames.ActivityCancelled ? "activity-cancelled" : "runtime-removed";
            var mutation = new RuntimeHistoryBookmarkMutation(ActivityExecutionBookmarkSummary.From(bookmark), status, reason,
                initialClaim ? selectedBookmark : null);
            Add(name, RuntimeHistoryEventCategory.BookmarkLifecycle, bookmark.BookmarkId, activity, bookmark.BookmarkId,
                new Dictionary<string, string> { ["history.stimulusType"] = bookmark.StimulusType, ["history.resumeTargetId"] = bookmark.ResumeTargetId },
                mutation, bookmark.ActivityExecutionId, bookmark.ExecutableNodeId);
        }

        if (policy.Version >= 2)
        {
            var explicitBookmarks = commit.StateChanges.Bookmarks.Select(change => change.StateId).ToHashSet(StringComparer.Ordinal);
            foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
            {
                var ownedBookmarks = cleanup.BookmarkIds.ToHashSet(StringComparer.Ordinal);
                var ownedActivities = cleanup.ActivityExecutionIds.ToHashSet(StringComparer.Ordinal);
                if (policy.SupportedKinds.Any(kind => kind is "BookmarkRemoved" or "BookmarkSuperseded") &&
                    ownedBookmarks.Except(explicitBookmarks).Except((cleanup.HistoryBookmarks ?? []).Select(summary => summary.Identity.BookmarkId)).Any())
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The cleanup owner did not supply its exact bookmark inventory.");
                foreach (var summary in cleanup.HistoryBookmarks ?? [])
                {
                    var identity = summary.Identity;
                    if (identity.WorkflowExecutionId != commit.WorkflowExecutionId || !ownedBookmarks.Contains(identity.BookmarkId) ||
                        !ownedActivities.Contains(identity.ActivityExecutionId))
                        throw new InvalidOperationException("History cleanup summary differs from its exact owned resource inventory.");
                    if (!explicitBookmarks.Add(identity.BookmarkId)) continue;
                    activities.TryGetValue(identity.ActivityExecutionId, out var activity);
                    var superseded = activity?.Status == ActivityExecutionStatus.Superseded && activity.SupersededByActivityExecutionId is not null;
                    var status = superseded ? RuntimeHistoryBookmarkStatus.Superseded : RuntimeHistoryBookmarkStatus.Removed;
                    var reason = superseded ? "activity-superseded" : activity?.Status == ActivityExecutionStatus.Cancelled ? "scope-cancelled" : "scope-cleanup";
                    var metadata = new Dictionary<string, string> { ["history.stimulusType"] = summary.StimulusType, ["history.resumeTargetId"] = identity.ResumeTargetId };
                    if (superseded) metadata["history.relatedActivityExecutionId"] = activity!.SupersededByActivityExecutionId!;
                    var mutation = new RuntimeHistoryBookmarkMutation(summary, status, reason)
                    { RelatedActivityExecutionId = superseded ? activity!.SupersededByActivityExecutionId : null };
                    Add(superseded ? "BookmarkSuperseded" : "BookmarkRemoved", RuntimeHistoryEventCategory.BookmarkLifecycle,
                        identity.BookmarkId, activity, identity.BookmarkId, metadata, mutation, identity.ActivityExecutionId);
                }
            }
        }

        foreach (var change in commit.StateChanges.Incidents)
            if (!incidentFactIds.Contains(change.StateId))
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The incident owner did not supply an explicit lifecycle or decision contribution.");
        EmitFacts(null, incidentPhase: true);
        foreach (var change in commit.StateChanges.WorkflowDispatches.OrderBy(change => change.StateId, StringComparer.Ordinal))
            Add("WorkflowDispatchChanged", RuntimeHistoryEventCategory.SchedulerLifecycle, change.StateId,
                metadata: new Dictionary<string, string> { ["history.status"] = change.State.Status.ToString() });
        if (policy.Version >= 2)
            foreach (var change in commit.StateChanges.DurableValues.OrderBy(change => change.StateId, StringComparer.Ordinal))
            {
                var value = change.State;
                if (value.WorkflowExecutionId != commit.WorkflowExecutionId || !Enum.IsDefined(value.Lifecycle) || !Enum.IsDefined(value.Storage) || !Enum.IsDefined(change.Operation))
                    throw new InvalidOperationException("Durable-state history requires exact typed owner, operation and storage policy.");
                activities.TryGetValue(value.SourceActivityExecutionId ?? "", out var activity);
                var address = RuntimeHistoryIdentity.CreateBinding([value.WorkflowExecutionId, value.DurableValueId, value.ValueId]);
                Add(change.Operation == RuntimeStateChangeOperation.Delete ? "DurableStateRemoved" : "DurableStateRecorded",
                    RuntimeHistoryEventCategory.ValueLifecycle, address, activity,
                    metadata: new Dictionary<string, string> {
                        ["history.durableStateAddress"] = address, ["history.operation"] = change.Operation.ToString(),
                        ["history.lifecycle"] = value.Lifecycle.ToString(), ["history.storage"] = value.Storage.ToString(),
                        ["history.sourceCapturedAt"] = value.CapturedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
                    owner: value.SourceActivityExecutionId,
                    occurredAt: change.Operation == RuntimeStateChangeOperation.Delete ? checkpoint.OccurredAt : value.CapturedAt);
            }

        if (!orderedContinuation) EmitValues(null);
        if (checkpoint.Name is RuntimeCheckpointNames.IncidentResolutionBatchApplied or RuntimeCheckpointNames.RuntimeAlterationJob)
            Add(checkpoint.Name, RuntimeHistoryEventCategory.OperationalLifecycle, commit.CommitId, owner: selectedActivity);

        return new RuntimeHistoryBatch(policy, checkpoint.CheckpointId, checkpoint.OccurredAt,
            checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted || checkpoint.HistoryStartsAtAdmission == true, result) { SourceFingerprint = SourceFingerprint(commit) };

        static bool IsBeforeOutput(RuntimeHistorySourceFact fact) => fact.Kind is
            RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationReturned;
        static bool IsIncidentFact(RuntimeHistorySourceFactKind kind) => kind is RuntimeHistorySourceFactKind.IncidentCreated or
            RuntimeHistorySourceFactKind.IncidentResolved or RuntimeHistorySourceFactKind.IncidentStrategyEvaluated or RuntimeHistorySourceFactKind.IncidentProjectionReconciled or
            RuntimeHistorySourceFactKind.IncidentSuppressed or RuntimeHistorySourceFactKind.IncidentDecisionApplied;
        void EmitValues(bool? outputsOnly)
        {
            if (!policy.SupportedKinds.Contains("ValueCaptured", StringComparer.Ordinal)) return;
            foreach (var change in commit.StateChanges.ActivityExecutionInspections.OrderBy(change => change.StateId, StringComparer.Ordinal))
            {
                activities.TryGetValue(change.StateId, out var activity);
                var projection = change.State;
                var contribution = projection.CheckpointValueCapture;
                if (projection.WorkflowExecutionId != commit.WorkflowExecutionId || projection.ActivityExecutionId != change.StateId)
                    throw new InvalidOperationException("Inspection history must retain its exact typed owner.");
                if (contribution is null && projection.ValueSnapshots.Count == 0) continue;
                if (contribution is null || contribution.CheckpointId != checkpoint.CheckpointId || projection.LastCheckpointId != checkpoint.CheckpointId ||
                    contribution.SchedulerWorkItemId is { } actualWork && actualWork != Read(RuntimeMetadataKeys.SchedulerWorkItemId) && actualWork != checkpoint.HistoryCausation?.SchedulerWorkItemId)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The inspection owner did not supply its exact checkpoint value contribution.");
                var contributed = contribution.EvidenceIds.Select(id =>
                    projection.ValueSnapshots.SingleOrDefault(value => value.EvidenceId == id)
                    ?? throw new InvalidOperationException("A checkpoint value contribution references unavailable exact evidence.")).ToArray();
                foreach (var value in contributed.Where(value => outputsOnly is null || (value.Subject != ActivityExecutionInspectionValueSubject.ActivityInput) == outputsOnly))
                {
                    var metadata = new Dictionary<string, string> { ["history.valueEvidenceIds"] = JsonSerializer.Serialize(new[] { value.EvidenceId }) };
                    if (policy.Version >= 2)
                    {
                        if (value.Phase is { } phase) metadata["history.valuePhase"] = phase;
                        if (value.EvaluationId is { } evaluation) metadata["history.valueEvaluationId"] = evaluation;
                    }
                    Add("ValueCaptured", RuntimeHistoryEventCategory.ValueLifecycle, value.EvidenceId!, activity,
                        metadata: metadata, owner: change.StateId, node: change.State.ExecutableNodeId, occurredAt: value.CapturedAt);
                }
            }
        }
        void EmitFacts(bool? beforeOutput, bool incidentPhase = false)
        {
            foreach (var fact in sourceFacts.Where(fact => IsIncidentFact(fact.Kind) == incidentPhase &&
                         (beforeOutput is null || IsBeforeOutput(fact) == beforeOutput)))
            {
                activities.TryGetValue(fact.ActivityExecutionId ?? "", out var activity);
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                if (fact.RelatedWorkflowExecutionId is { } relatedRun) metadata["history.relatedWorkflowExecutionId"] = relatedRun;
                if (fact.RelatedActivityExecutionId is { } relatedActivity) metadata["history.relatedActivityExecutionId"] = relatedActivity;
                if (fact.TimerId is { } timer) metadata["history.timerId"] = timer;
                if (fact.CompensationEntryId is { } entry) metadata["history.compensationEntryId"] = entry;
                if (fact.ReasonCode is { } reason) metadata["history.reasonCode"] = reason;
                if (fact.OutcomeNames.Count > 0)
                {
                    var outcomes = JsonSerializer.Serialize(fact.OutcomeNames);
                    if (outcomes.Length <= 2048) metadata["history.outcomeNames"] = outcomes;
                    else
                    {
                        metadata["history.outcomeCaptureState"] = "size-omitted";
                        metadata["history.outcomeCount"] = fact.OutcomeNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        metadata["history.outcomeContentHash"] = RuntimeHistoryIdentity.CreateBinding(fact.OutcomeNames);
                    }
                }
                if (IsIncidentFact(fact.Kind))
                {
                    incidents.TryGetValue(fact.IncidentId ?? "", out var incident);
                    if (fact.SubjectId != fact.IncidentId || incident is null && fact.Kind != RuntimeHistorySourceFactKind.IncidentProjectionReconciled || incident is not null &&
                        (incident.WorkflowExecutionId != commit.WorkflowExecutionId || incident.ActivityExecutionId != fact.ActivityExecutionId ||
                         fact.Kind == RuntimeHistorySourceFactKind.IncidentCreated && fact.OccurredAt != incident.CreatedAt ||
                         fact.Kind == RuntimeHistorySourceFactKind.IncidentResolved && (incident.Status != IncidentStatus.Resolved || incident.ResolvedAt is null || fact.OccurredAt != incident.ResolvedAt) ||
                         fact.Kind == RuntimeHistorySourceFactKind.IncidentSuppressed && (incident.Status != IncidentStatus.Suppressed || incident.ResolvedAt is null || fact.OccurredAt != incident.ResolvedAt) ||
                         (fact.Kind is RuntimeHistorySourceFactKind.IncidentStrategyEvaluated or RuntimeHistorySourceFactKind.IncidentDecisionApplied) && (incident.ResolutionOutcome is null ||
                             fact.OccurredAt != incident.ResolutionOutcome.AppliedAt || fact.ReasonCode != (incident.ResolutionOutcome.ActionKind.Length <= 128 ? incident.ResolutionOutcome.ActionKind : null))))
                        throw new InvalidOperationException("Incident source facts disagree with their actual typed lifecycle or decision.");
                    if (incident is not null) metadata["history.status"] = incident.Status.ToString();
                }
                var category = fact.Kind switch
                {
                    RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationSuspended or
                        RuntimeHistorySourceFactKind.ActivityContinuationCompleted or RuntimeHistorySourceFactKind.ActivityContinuationFaulted or
                        RuntimeHistorySourceFactKind.ActivityContinuationCancelled or RuntimeHistorySourceFactKind.ActivityContinuationReturned or RuntimeHistorySourceFactKind.ActivityRetryScheduled => RuntimeHistoryEventCategory.ActivityLifecycle,
                    RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested or RuntimeHistorySourceFactKind.ChildWorkflowSettled or
                        RuntimeHistorySourceFactKind.StimulusPublicationCommitted or RuntimeHistorySourceFactKind.TimerCancellationRequested => RuntimeHistoryEventCategory.SchedulerLifecycle,
                    RuntimeHistorySourceFactKind.IncidentResolutionApplied or RuntimeHistorySourceFactKind.IncidentCreated or
                        RuntimeHistorySourceFactKind.IncidentResolved or RuntimeHistorySourceFactKind.IncidentStrategyEvaluated or RuntimeHistorySourceFactKind.IncidentProjectionReconciled or
                        RuntimeHistorySourceFactKind.IncidentSuppressed or RuntimeHistorySourceFactKind.IncidentDecisionApplied => RuntimeHistoryEventCategory.IncidentLifecycle,
                    RuntimeHistorySourceFactKind.DurableStateRecorded => RuntimeHistoryEventCategory.ValueLifecycle,
                    RuntimeHistorySourceFactKind.ChildControlAccepted or RuntimeHistorySourceFactKind.ChildControlApplied or RuntimeHistorySourceFactKind.RecoveryRequested or
                        RuntimeHistorySourceFactKind.ControlApplied or RuntimeHistorySourceFactKind.AlterationApplied or
                        RuntimeHistorySourceFactKind.CompensationRegistered or RuntimeHistorySourceFactKind.CompensationStarted or
                        RuntimeHistorySourceFactKind.CompensationHandlerScheduled or RuntimeHistorySourceFactKind.CompensationHandlerCompleted or
                        RuntimeHistorySourceFactKind.CompensationHandlerFaulted or RuntimeHistorySourceFactKind.CompensationConfirmed or
                        RuntimeHistorySourceFactKind.CompensationResolved or RuntimeHistorySourceFactKind.CompensationCompleted or RuntimeHistorySourceFactKind.CompensationAbandoned => RuntimeHistoryEventCategory.OperationalLifecycle,
                    _ => throw new InvalidOperationException("History has no semantic catalogue entry for this source fact.")
                };
                Add(RuntimeHistorySourceFacts.EventName(fact.Kind), category, fact.SubjectId, activity, metadata: metadata, owner: fact.ActivityExecutionId,
                    incidentId: fact.IncidentId, occurredAt: fact.OccurredAt, sourceAttempt: fact.AttemptId, sourceCause: fact.Causation, occurredAtKnown: fact.OccurredAtKnown);
            }
        }

        string? Read(string key) => checkpoint.Metadata.TryGetValue(key, out var value) ? value : commit.Metadata.TryGetValue(key, out value) ? value : null;
        void Add(string name, RuntimeHistoryEventCategory category, string subject, ActivityExecutionState? activity = null,
            string? bookmarkId = null, IReadOnlyDictionary<string, string>? metadata = null, RuntimeHistoryBookmarkMutation? mutation = null,
            string? owner = null, string? node = null, string? incidentId = null, DateTimeOffset? occurredAt = null,
            string? sourceAttempt = null, RuntimeHistoryCausation? sourceCause = null, bool? occurredAtKnown = null)
        {
            if (!policy.SupportedKinds.Contains(name, StringComparer.Ordinal)) return;
            var ordinal = result.Count;
            var id = RuntimeHistoryIdentity.CreateId(checkpoint.CheckpointId, name, subject);
            var e = new RuntimeHistoryEvent(id, commit.WorkflowExecutionId, 0, category, name, occurredAt ?? checkpoint.OccurredAt,
                activity?.Execution.ActivityExecutionId ?? owner, activity?.Execution.ExecutableNodeId ?? node,
                checkpoint.CheckpointId, incidentId, metadata: metadata)
            {
                Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = bookmarkId, OccurredAtKnown = occurredAtKnown,
                ExecutionScopeId = activity?.ExecutionScopeId ?? activity?.Provenance.ExecutionScopeId,
                IterationId = activity?.Provenance.IterationId, ParentActivityExecutionId = activity?.Provenance.ParentActivityExecutionId, ActivityAncestryKnown = activity is not null && !(policy.PolicyId == "runtime-history-v1" && policy.Version == 1) ? true : null,
                AttemptId = sourceAttempt ?? (activity?.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attempt) == true ? attempt : null),
                Causation = sourceCause ?? cause
            };
            result.Add(new(e, ordinal, mutation));
        }
    }

    private static string SourceFingerprint(RuntimeCheckpointCommit commit) => RuntimeCheckpointCommitFingerprint.Compute(
        commit with { HistoryBatches = null });
}
