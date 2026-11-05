using Elsa.Workflows.Runtime.Core.Exceptions;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Captures immutable semantic candidates before coalescing; it never writes a store.</summary>
internal sealed class RuntimeHistoryCapture(IWorkflowExecutionStateStore executions, IActivityExecutionInspectionStore? inspections = null,
    IEnumerable<RuntimeHistorySourceSupport>? sourceSupport = null, RuntimeHistorySourceAvailability? sourceAvailability = null)
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
        if (!(policy.PolicyId == "runtime-history-v1" && policy.Version == 1 || policy.PolicyId == "runtime-history-v2" && policy.Version == 2)) return false;
        var available = Kinds.ToHashSet(StringComparer.Ordinal);
        var owners = (sourceSupport ?? []).ToArray();
        if (policy.Version >= 2) { available.UnionWith(Version2Kinds); available.UnionWith(owners.SelectMany(owner => owner.Kinds).Select(kind => kind.ToString())); }
        if (policy.SupportedKinds.Intersect(RuntimeHistoryDeliveryFacts.Kinds, StringComparer.Ordinal).Any() && sourceAvailability?.Delivery() == true)
            available.UnionWith(RuntimeHistoryDeliveryFacts.Kinds);
        if (owners.Any(owner => owner.TimerOwner) && policy.SupportedKinds.Intersect(DurableTimerHistoryFacts.Kinds, StringComparer.Ordinal).Any() && sourceAvailability?.Timer() == true)
            available.UnionWith(DurableTimerHistoryFacts.Kinds);
        return policy.SupportedKinds.All(available.Contains);
    }

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.HistoryBatches is { Count: > 0 } || IsFlush(commit)) return commit;
        var policy = await PolicyAsync(commit, cancellationToken);
        if (policy is null) return commit.HistorySourceFacts is null ? commit : commit with { HistorySourceFacts = null };
        if (sourceSupport is not null && !CanCapture(policy)) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The current runtime cannot capture the retained history profile.");
        if (policy.PolicyId == "runtime-history-v1" && policy.Version == 1 && commit.HistorySourceFacts is not null)
            commit = commit with { HistorySourceFacts = null };
        var previousEvidence = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var change in commit.StateChanges.ActivityExecutionInspections)
        {
            if (change.State.ValueSnapshots.Count == 0) continue;
            if (inspections is null) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "History capture requires its activity inspection dependency.");
            var prior = await ReadAsync(() => inspections.FindAsync(commit.WorkflowExecutionId, change.StateId, cancellationToken));
            previousEvidence[change.StateId] = (prior?.ValueSnapshots ?? []).Where(value => value.EvidenceId is not null)
                .Select(value => value.EvidenceId!).ToHashSet(StringComparer.Ordinal);
        }
        var batch = Build(commit, policy, (activity, value) => !previousEvidence.TryGetValue(activity, out var recorded) || !recorded.Contains(value));
        return commit with { HistoryBatches = [batch] };
    }

    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.HistoryBatches is not { Count: > 0 } batches) return;
        foreach (var batch in batches) RuntimeHistorySafety.Validate(batch, commit.WorkflowExecutionId);
        if (IsFlush(commit)) return;
        if (batches.Count != 1 || batches[0].SourceFingerprint != SourceFingerprint(commit))
            throw new InvalidOperationException("The logical checkpoint source changed after semantic history capture.");
        var policy = await PolicyAsync(commit, cancellationToken);
        if (policy is null || batches.Count != 1 || !policy.IsEquivalentTo(batches[0].Policy))
            throw new InvalidOperationException("History must retain the actual admitted execution's capture policy.");
        // A replay must retain its original delta even after its values became the current inspection projection.
        // Revalidate each retained link against the actual typed source evidence rather than inventing a new delta.
        var retainedValues = batches[0].Candidates.Where(candidate => candidate.Event.Name == "ValueCaptured")
            .Select(candidate => candidate.Event.HistoryEventId).ToHashSet(StringComparer.Ordinal);
        var expected = Build(commit, policy, (_, value) => retainedValues.Contains(RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, "ValueCaptured", value)));
        if (!expected.Candidates.Select(RuntimeHistoryIdentity.Compute)
                .SequenceEqual(batches[0].Candidates.Select(RuntimeHistoryIdentity.Compute), StringComparer.Ordinal))
            throw new InvalidOperationException("The final checkpoint no longer agrees with its semantic history candidates.");
    }

    private async ValueTask<RuntimeHistoryCapturePolicy?> PolicyAsync(RuntimeCheckpointCommit commit, CancellationToken token)
    {
        var admitted = await ReadAsync(() => executions.FindAsync(commit.WorkflowExecutionId, token));
        if (admitted is not null) return admitted.HistoryCapturePolicy;
        if (commit.StateChanges.WorkflowExecution is { } change) return change.State.HistoryCapturePolicy;
        return null;
    }

    private static async ValueTask<T> ReadAsync<T>(Func<ValueTask<T>> read)
    {
        try { return await read(); }
        catch (Exception exception) when (exception is not (OperationCanceledException or ArgumentException or InvalidDataException or RuntimeSchedulerWorkUnavailableException))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "History capture dependency is unavailable.", exception); }
    }

    private static bool IsFlush(RuntimeCheckpointCommit commit) => commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);

    private static RuntimeHistoryBatch Build(RuntimeCheckpointCommit commit, RuntimeHistoryCapturePolicy policy, Func<string, string, bool> includeValue)
    {
        var result = new List<RuntimeHistoryCandidate>();
        var checkpoint = commit.Checkpoint;
        if (checkpoint.HistoryStartsAtAdmission == true && (policy.Version < 2 || checkpoint.Name != RuntimeCheckpointNames.WorkflowFaulted ||
            commit.StateChanges.WorkflowExecution?.State is not { Status: WorkflowExecutionStatus.Faulted, HistoryCaptureEnabledAtAdmission: true }))
            throw new InvalidOperationException("Initial fault capture requires the actual readable admitted start boundary.");
        var activities = commit.StateChanges.ActivityExecutions.ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var selectedBookmark = Read(RuntimeMetadataKeys.BookmarkId);
        var selectedActivity = Read(RuntimeMetadataKeys.ActivityExecutionId);
        var cause = checkpoint.HistoryCausation ?? new RuntimeHistoryCausation(CommandId: Read(RuntimeMetadataKeys.CommandId), SchedulerWorkItemId: Read(RuntimeMetadataKeys.SchedulerWorkItemId));

        var orderedContinuation = policy.Version >= 2 && (commit.HistorySourceFacts ?? []).Any(fact => fact.Kind is
            RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationReturned);
        if (orderedContinuation)
        {
            EmitValues(false);
            EmitFacts(true);
            EmitValues(true);
            EmitFacts(false);
        }
        else if (policy.Version >= 2) EmitFacts(null);

        if (commit.StateChanges.WorkflowExecution is { } workflow &&
            checkpoint.Name is RuntimeCheckpointNames.WorkflowStarted or RuntimeCheckpointNames.WorkflowCompleted or RuntimeCheckpointNames.WorkflowFaulted or RuntimeCheckpointNames.WorkflowCancelled or RuntimeCheckpointNames.WorkflowSuspended)
            Add(checkpoint.Name, RuntimeHistoryEventCategory.WorkflowLifecycle, commit.WorkflowExecutionId,
                metadata: new Dictionary<string, string> { ["history.status"] = workflow.State.Status.ToString() });

        foreach (var change in commit.StateChanges.ActivityExecutions.OrderBy(change => change.State.ExecutionSequence).ThenBy(change => change.StateId, StringComparer.Ordinal))
        {
            var a = change.State;
            if (checkpoint.Name is not (RuntimeCheckpointNames.ActivityScheduled or RuntimeCheckpointNames.ActivityStarted or
                RuntimeCheckpointNames.ActivityAttemptClaimed or RuntimeCheckpointNames.ActivityCompleted or RuntimeCheckpointNames.IntrinsicCompleted or
                RuntimeCheckpointNames.ActivitySuspended or RuntimeCheckpointNames.ActivityCancelled or RuntimeCheckpointNames.ActivityRecovered or RuntimeCheckpointNames.IncidentRecorded))
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
                            (commit.StateChanges.Bookmarks.Any(b => b.Operation == RuntimeStateChangeOperation.Delete) ? "ActivityResumeClaimed" : "ActivityResumeRetried") : "ActivityAttemptClaimed") : null
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
                foreach (var summary in cleanup.HistoryBookmarks ?? [])
                {
                    var identity = summary.Identity;
                    if (identity.WorkflowExecutionId != commit.WorkflowExecutionId || !cleanup.BookmarkIds.Contains(identity.BookmarkId, StringComparer.Ordinal) ||
                        !cleanup.ActivityExecutionIds.Contains(identity.ActivityExecutionId, StringComparer.Ordinal))
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

        foreach (var change in commit.StateChanges.Incidents.OrderBy(change => change.StateId, StringComparer.Ordinal))
        {
            activities.TryGetValue(change.State.ActivityExecutionId ?? "", out var activity);
            Add(change.State.ResolvedAt is null ? "IncidentRecorded" : "IncidentResolved", RuntimeHistoryEventCategory.IncidentLifecycle,
                change.StateId, activity, incidentId: change.StateId, owner: change.State.ActivityExecutionId,
                node: change.State.ExecutableNodeId);
        }
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
        void EmitValues(bool? outputsOnly)
        {
        foreach (var change in commit.StateChanges.ActivityExecutionInspections.OrderBy(change => change.StateId, StringComparer.Ordinal))
        {
            activities.TryGetValue(change.StateId, out var activity);
            foreach (var value in change.State.ValueSnapshots.Where(value => !string.IsNullOrWhiteSpace(value.EvidenceId) && includeValue(change.StateId, value.EvidenceId!) && (outputsOnly is null || (value.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput) == outputsOnly)).OrderBy(value => policy.Version >= 2 ? value.Sequence ?? long.MaxValue : 0).ThenBy(value => value.EvidenceId, StringComparer.Ordinal))
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
        void EmitFacts(bool? beforeOutput)
        {
        foreach (var fact in (commit.HistorySourceFacts ?? []).Where(fact => beforeOutput is null || IsBeforeOutput(fact) == beforeOutput))
        {
            activities.TryGetValue(fact.ActivityExecutionId ?? "", out var activity);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            if (fact.RelatedWorkflowExecutionId is { } relatedRun) metadata["history.childWorkflowId"] = relatedRun;
            if (fact.RelatedActivityExecutionId is { } relatedActivity) metadata["history.relatedActivityExecutionId"] = relatedActivity;
            if (fact.TimerId is { } timer) metadata["history.timerId"] = timer;
            if (fact.CompensationEntryId is { } entry) metadata["history.compensationEntryId"] = entry;
            if (fact.ReasonCode is { } reason) metadata["history.reasonCode"] = reason;
            if (fact.OutcomeNames.Count > 0) metadata["history.outcomeNames"] = JsonSerializer.Serialize(fact.OutcomeNames);
            var category = fact.Kind switch
            {
                RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationSuspended or
                    RuntimeHistorySourceFactKind.ActivityContinuationCompleted or RuntimeHistorySourceFactKind.ActivityContinuationFaulted or
                    RuntimeHistorySourceFactKind.ActivityContinuationCancelled or RuntimeHistorySourceFactKind.ActivityContinuationReturned or RuntimeHistorySourceFactKind.ActivityRetryScheduled => RuntimeHistoryEventCategory.ActivityLifecycle,
                RuntimeHistorySourceFactKind.ChildWorkflowDispatched or RuntimeHistorySourceFactKind.ChildWorkflowSettled or
                    RuntimeHistorySourceFactKind.StimulusPublished or RuntimeHistorySourceFactKind.TimerCancellationRequested => RuntimeHistoryEventCategory.SchedulerLifecycle,
                RuntimeHistorySourceFactKind.IncidentResolutionApplied => RuntimeHistoryEventCategory.IncidentLifecycle,
                RuntimeHistorySourceFactKind.ChildControlAccepted or RuntimeHistorySourceFactKind.ChildControlApplied or RuntimeHistorySourceFactKind.RecoveryRequested or
                    RuntimeHistorySourceFactKind.ControlApplied or RuntimeHistorySourceFactKind.AlterationApplied or
                    RuntimeHistorySourceFactKind.CompensationRegistered or RuntimeHistorySourceFactKind.CompensationStarted or
                    RuntimeHistorySourceFactKind.CompensationHandlerScheduled or RuntimeHistorySourceFactKind.CompensationHandlerCompleted or
                    RuntimeHistorySourceFactKind.CompensationHandlerFaulted or RuntimeHistorySourceFactKind.CompensationConfirmed or
                    RuntimeHistorySourceFactKind.CompensationResolved or RuntimeHistorySourceFactKind.CompensationCompleted => RuntimeHistoryEventCategory.OperationalLifecycle,
                _ => throw new InvalidOperationException("History has no semantic catalogue entry for this source fact.")
            };
            Add(fact.Kind.ToString(), category, fact.SubjectId, activity, metadata: metadata, owner: fact.ActivityExecutionId,
                incidentId: fact.IncidentId, occurredAt: fact.OccurredAt, sourceAttempt: fact.AttemptId, sourceCause: fact.Causation);
        }
        }

        string? Read(string key) => checkpoint.Metadata.TryGetValue(key, out var value) ? value : commit.Metadata.TryGetValue(key, out value) ? value : null;
        void Add(string name, RuntimeHistoryEventCategory category, string subject, ActivityExecutionState? activity = null,
            string? bookmarkId = null, IReadOnlyDictionary<string, string>? metadata = null, RuntimeHistoryBookmarkMutation? mutation = null,
            string? owner = null, string? node = null, string? incidentId = null, DateTimeOffset? occurredAt = null,
            string? sourceAttempt = null, RuntimeHistoryCausation? sourceCause = null)
        {
            if (!policy.SupportedKinds.Contains(name, StringComparer.Ordinal)) return;
            var ordinal = result.Count;
            var id = RuntimeHistoryIdentity.CreateId(checkpoint.CheckpointId, name, subject);
            var e = new RuntimeHistoryEvent(id, commit.WorkflowExecutionId, 0, category, name, occurredAt ?? checkpoint.OccurredAt,
                activity?.Execution.ActivityExecutionId ?? owner, activity?.Execution.ExecutableNodeId ?? node,
                checkpoint.CheckpointId, incidentId, metadata: metadata)
            {
                Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = bookmarkId,
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
