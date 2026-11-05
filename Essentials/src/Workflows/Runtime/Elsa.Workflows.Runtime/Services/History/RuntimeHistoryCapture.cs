using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Captures immutable semantic candidates before coalescing; it never writes a store.</summary>
internal sealed class RuntimeHistoryCapture(IWorkflowExecutionStateStore executions, IActivityExecutionInspectionStore? inspections = null) : IRuntimeCheckpointCommitEnricher
{
    internal static readonly string[] Kinds =
    [
        "WorkflowStarted", "WorkflowCompleted", "WorkflowFaulted", "WorkflowCancelled", "WorkflowSuspended",
        "ActivityScheduled", "ActivityStarted", "ActivityAttemptClaimed", "ActivityResumeClaimed", "ActivityResumeRetried",
        "ActivityCompleted", "ActivitySuspended", "ActivityCancelled", "ActivityRecovered", "ActivityFaulted",
        "BookmarkCreated", "BookmarkConsumed", "BookmarkRetired", "BookmarkRemoved", "BookmarkStaleRemoved",
        "IncidentRecorded", "IncidentResolved", "IncidentResolutionBatchApplied", "WorkflowDispatchChanged", "ValueCaptured", "RuntimeAlterationJob"
    ];

    public int Order => 2000;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.StateChanges.HistoryBatches is { Count: > 0 } || IsFlush(commit)) return commit;
        var policy = await PolicyAsync(commit, cancellationToken);
        if (policy is null) return commit; // Legacy queued/admitted work is never opted in by a host toggle.
        var previousEvidence = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var change in commit.StateChanges.ActivityExecutionInspections)
        {
            if (change.State.ValueSnapshots.Count == 0) continue;
            if (inspections is null) throw new NotSupportedException("Value capture history requires the actual activity inspection reader.");
            var prior = await inspections.FindAsync(commit.WorkflowExecutionId, change.StateId, cancellationToken);
            previousEvidence[change.StateId] = (prior?.ValueSnapshots ?? []).Where(value => value.EvidenceId is not null)
                .Select(value => value.EvidenceId!).ToHashSet(StringComparer.Ordinal);
        }
        var batch = Build(commit, policy, (activity, value) => !previousEvidence.TryGetValue(activity, out var recorded) || !recorded.Contains(value));
        return commit with { StateChanges = commit.StateChanges.WithHistoryBatches([batch]) };
    }

    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.StateChanges.HistoryBatches is not { Count: > 0 } batches) return;
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
        var admitted = await executions.FindAsync(commit.WorkflowExecutionId, token);
        if (admitted is not null) return admitted.HistoryCapturePolicy;
        if (commit.StateChanges.WorkflowExecution is { } change) return change.State.HistoryCapturePolicy;
        return null;
    }

    private static bool IsFlush(RuntimeCheckpointCommit commit) => commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush);

    private static RuntimeHistoryBatch Build(RuntimeCheckpointCommit commit, RuntimeHistoryCapturePolicy policy, Func<string, string, bool> includeValue)
    {
        var result = new List<RuntimeHistoryCandidate>();
        var checkpoint = commit.Checkpoint;
        var activities = commit.StateChanges.ActivityExecutions.ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var selectedBookmark = Read(RuntimeMetadataKeys.BookmarkId);
        var selectedActivity = Read(RuntimeMetadataKeys.ActivityExecutionId);
        var cause = new RuntimeHistoryCausation(CommandId: Read(RuntimeMetadataKeys.CommandId), SchedulerWorkItemId: Read(RuntimeMetadataKeys.SchedulerWorkItemId));

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
        foreach (var change in commit.StateChanges.ActivityExecutionInspections.OrderBy(change => change.StateId, StringComparer.Ordinal))
        {
            activities.TryGetValue(change.StateId, out var activity);
            foreach (var value in change.State.ValueSnapshots.Where(value => !string.IsNullOrWhiteSpace(value.EvidenceId) && includeValue(change.StateId, value.EvidenceId!)).OrderBy(value => value.EvidenceId, StringComparer.Ordinal))
                Add("ValueCaptured", RuntimeHistoryEventCategory.ValueLifecycle, value.EvidenceId!, activity,
                    metadata: new Dictionary<string, string> { ["history.valueEvidenceIds"] = JsonSerializer.Serialize(new[] { value.EvidenceId }) }, owner: change.StateId, node: change.State.ExecutableNodeId, occurredAt: value.CapturedAt);
        }
        if (checkpoint.Name is RuntimeCheckpointNames.IncidentResolutionBatchApplied or RuntimeCheckpointNames.RuntimeAlterationJob)
            Add(checkpoint.Name, RuntimeHistoryEventCategory.OperationalLifecycle, commit.CommitId, owner: selectedActivity);

        return new RuntimeHistoryBatch(policy, checkpoint.CheckpointId, checkpoint.OccurredAt,
            checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted, result) { SourceFingerprint = SourceFingerprint(commit) };

        string? Read(string key) => checkpoint.Metadata.TryGetValue(key, out var value) ? value : commit.Metadata.TryGetValue(key, out value) ? value : null;
        void Add(string name, RuntimeHistoryEventCategory category, string subject, ActivityExecutionState? activity = null,
            string? bookmarkId = null, IReadOnlyDictionary<string, string>? metadata = null, RuntimeHistoryBookmarkMutation? mutation = null,
            string? owner = null, string? node = null, string? incidentId = null, DateTimeOffset? occurredAt = null)
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
                IterationId = activity?.Provenance.IterationId, ParentActivityExecutionId = activity?.Provenance.ParentActivityExecutionId,
                AttemptId = activity?.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attempt) == true ? attempt : null,
                Causation = cause
            };
            result.Add(new(e, ordinal, mutation));
        }
    }

    private static string SourceFingerprint(RuntimeCheckpointCommit commit) => RuntimeCheckpointCommitFingerprint.Compute(
        commit with { StateChanges = commit.StateChanges.WithHistoryBatches(null) });
}
