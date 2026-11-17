using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>
/// Spec 1221 FR-024: numbers each logical checkpoint of an instance and stamps the number on what it commits: new
/// variable write records, the activity lifecycle transitions it makes, and an alteration's variable writes. It runs in
/// the committer under the execution lease, before a coalescing drain folds checkpoints, so every logical checkpoint
/// has its own number.
/// </summary>
/// <remarks>
/// <para>
/// The number is the lease's fencing token (issued one higher on every acquisition) followed by the commit's ordinal
/// within the lease, so it grows across owners without a stored counter, and costs no read or write. A commit outside
/// a lease is not numbered.
/// </para>
/// <para>
/// A handler may commit a state it built from an earlier, unstamped copy within the same lease. Stamps the lease made
/// in a commit the store persisted are merged back in by identity, so a persisted stamp is never lost or renumbered;
/// stamps of a commit that was skipped, refused or failed are never merged into a later one
/// (<see cref="Acknowledge"/>). The same commit submitted twice in a lease gets the same number. What the lease
/// remembers for an activity is dropped once the activity ends, and only the most recent commit ids are kept.
/// </para>
/// <para>
/// Known limit: a commit object resubmitted under a later lease (after the earlier owner lost its lease) is numbered
/// again, so its fingerprint differs from the stored one. Rebuilt commits already differ by their timestamps, so this
/// only matters for a byte-identical replay across owners.
/// </para>
/// </remarks>
internal static class RuntimeCommitSequenceStamper
{
    private const int RememberedCommits = 1024;
    private static readonly ConditionalWeakTable<RuntimeExecutionLease, LeaseSequence> Leases = new();

    public static RuntimeCheckpointCommit Stamp(RuntimeCheckpointCommit commit, RuntimeExecutionLease? lease)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var activities = commit.StateChanges.ActivityExecutions;
        var job = commit.StateChanges.AlterationJobTerminalChange;
        if (activities.Count == 0 && job?.Outcomes.All(outcome => outcome.VariableWrites is null) != false)
            return commit;
        if (lease is null || !StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return WithoutPendingNumbers(commit, activities, job);

        var state = Leases.GetValue(lease, _ => new LeaseSequence());
        lock (state)
        {
            var sequence = state.SequenceFor(commit.CommitId, lease.FencingToken);
            var stamped = activities.Select(change => change.State is null ? change : change with { State = state.StampActivity(change.State, sequence) }).ToArray();
            var changes = commit.StateChanges.WithActivityExecutions(stamped);
            if (job is not null && job.Outcomes.Any(outcome => outcome.VariableWrites is not null))
                changes = changes.WithAlterationJobTerminalChange(new WorkflowAlterationJobTerminalChange(
                    job.JobId,
                    job.ClaimToken,
                    job.Status,
                    job.Outcomes.Select(outcome => outcome.VariableWrites is null
                        ? outcome
                        : outcome with { VariableWrites = outcome.VariableWrites.Select(record => record.CommitSequence == VariableWriteRecord.PendingCommitSequence ? record with { CommitSequence = sequence } : record).ToArray() }).ToArray(),
                    job.CheckpointCommitId,
                    job.CompletedAt,
                    job.SafeFailure));
            return commit with { StateChanges = changes };
        }
    }

    // Outside a lease nothing is numbered: a record built for this commit leaves without a number, as order unknown.
    private static RuntimeCheckpointCommit WithoutPendingNumbers(
        RuntimeCheckpointCommit commit,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activities,
        WorkflowAlterationJobTerminalChange? job)
    {
        static VariableWriteRecord Clear(VariableWriteRecord record) =>
            record.CommitSequence == VariableWriteRecord.PendingCommitSequence ? record with { CommitSequence = null } : record;

        var changes = commit.StateChanges.WithActivityExecutions(activities
            .Select(change => change.State?.VariableWrites is { } writes && writes.Any(record => record.CommitSequence == VariableWriteRecord.PendingCommitSequence)
                ? change with { State = change.State with { VariableWrites = writes.Select(Clear).ToArray() } }
                : change)
            .ToArray());
        if (job is not null && job.Outcomes.Any(outcome => outcome.VariableWrites is not null))
            changes = changes.WithAlterationJobTerminalChange(new WorkflowAlterationJobTerminalChange(
                job.JobId,
                job.ClaimToken,
                job.Status,
                job.Outcomes.Select(outcome => outcome.VariableWrites is null ? outcome : outcome with { VariableWrites = outcome.VariableWrites.Select(Clear).ToArray() }).ToArray(),
                job.CheckpointCommitId,
                job.CompletedAt,
                job.SafeFailure));
        return commit with { StateChanges = changes };
    }

    /// <summary>
    /// Records that the store persisted <paramref name="commit"/> (as stamped by <see cref="Stamp"/>): its stamps and
    /// numbered records are now ones a later commit of this lease may merge back in.
    /// </summary>
    public static void Acknowledge(RuntimeCheckpointCommit commit, RuntimeExecutionLease? lease)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (lease is null || !Leases.TryGetValue(lease, out var state))
            return;
        lock (state)
            foreach (var activity in commit.StateChanges.ActivityExecutions.Select(change => change.State).OfType<ActivityExecutionState>())
                state.Acknowledge(activity);
    }

    /// <summary>The transition an activity's status stands for, given the transition before it.</summary>
    public static ActivityLifecycleTransitionKind? TransitionOf(ActivityExecutionStatus status, ActivityLifecycleTransitionKind? previous) => status switch
    {
        ActivityExecutionStatus.Scheduled => ActivityLifecycleTransitionKind.Scheduled,
        ActivityExecutionStatus.Running => previous == ActivityLifecycleTransitionKind.Suspended
            ? ActivityLifecycleTransitionKind.Resumed
            : previous is ActivityLifecycleTransitionKind.Started or ActivityLifecycleTransitionKind.Resumed ? previous : ActivityLifecycleTransitionKind.Started,
        ActivityExecutionStatus.Suspended or ActivityExecutionStatus.Waiting => ActivityLifecycleTransitionKind.Suspended,
        ActivityExecutionStatus.Completed => ActivityLifecycleTransitionKind.Completed,
        ActivityExecutionStatus.Faulted => ActivityLifecycleTransitionKind.Faulted,
        ActivityExecutionStatus.Cancelled => ActivityLifecycleTransitionKind.Cancelled,
        // The successor row records its own lifecycle from its own Scheduled or Started stamp.
        ActivityExecutionStatus.Superseded => ActivityLifecycleTransitionKind.Superseded,
        _ => null
    };

    private static bool IsTerminal(ActivityExecutionStatus status) =>
        status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded;

    private static string RecordKey(ActivityExecutionState activity, VariableWriteRecord record) =>
        string.Join('\u001f', activity.Execution.ActivityExecutionId, record.ScopeId, record.VariableKey,
            record.Writer.InvocationId, record.NewVersion, record.CommitOrder, record.Outcome, record.OccurredAt.UtcTicks);

    private sealed class LeaseSequence
    {
        private long _ordinal;
        private readonly Dictionary<string, long> _commits = new(StringComparer.Ordinal);
        private readonly Queue<string> _commitOrder = new();
        private readonly Dictionary<string, Persisted> _activities = new(StringComparer.Ordinal);

        public long SequenceFor(string commitId, long fencingToken)
        {
            if (_commits.TryGetValue(commitId, out var known))
                return known;
            var sequence = RuntimeCommitSequence.Compose(fencingToken, ++_ordinal);
            _commits[commitId] = sequence;
            _commitOrder.Enqueue(commitId);
            if (_commitOrder.Count > RememberedCommits)
                _commits.Remove(_commitOrder.Dequeue());
            return sequence;
        }

        public ActivityExecutionState StampActivity(ActivityExecutionState activity, long sequence)
        {
            _activities.TryGetValue(activity.Execution.ActivityExecutionId, out var persisted);
            // Only a record built for a commit in progress is numbered: one already numbered keeps its number, and one
            // without a number (older, or committed outside a lease) stays without, as order unknown.
            var records = activity.VariableWrites?.Select(record => record.CommitSequence != VariableWriteRecord.PendingCommitSequence
                    ? record
                    : record with
                    {
                        CommitSequence = persisted is not null && persisted.Records.TryGetValue(RecordKey(activity, record), out var number) ? number : sequence
                    })
                .ToArray();

            // Stamps a persisted commit of this lease made on an earlier copy come back, by kind and sequence.
            var stamps = (activity.Transitions ?? []).ToList();
            foreach (var stamp in persisted?.Stamps ?? [])
                if (!stamps.Any(existing => existing.Kind == stamp.Kind && existing.CommitSequence == stamp.CommitSequence))
                    stamps.Add(stamp);
            stamps.Sort((left, right) => left.CommitSequence.CompareTo(right.CommitSequence));

            var previous = stamps.Count == 0 ? (ActivityLifecycleTransitionKind?)null : stamps[^1].Kind;
            if (TransitionOf(activity.Status, previous) is { } kind && kind != previous)
            {
                var evaluation = activity.InputSnapshot?.EvaluationNumber is > 1 and var number ? number : (int?)null;
                stamps.Add(new ActivityLifecycleStamp(kind, sequence, activity.BranchId, activity.Provenance.ExecutionPathId, evaluation));
            }

            var numbers = stamps.Select(stamp => stamp.CommitSequence)
                .Concat((records ?? []).Where(record => record.CommitSequence is >= 0).Select(record => record.CommitSequence!.Value))
                .ToArray();
            return activity with
            {
                VariableWrites = records ?? activity.VariableWrites,
                Transitions = stamps.Count == 0 ? activity.Transitions : stamps,
                FirstCommitSequence = numbers.Length == 0 ? activity.FirstCommitSequence : numbers.Min(),
                LastCommitSequence = numbers.Length == 0 ? activity.LastCommitSequence : numbers.Max()
            };
        }

        public void Acknowledge(ActivityExecutionState activity)
        {
            var id = activity.Execution.ActivityExecutionId;
            // An ended activity is not committed again from a stale copy; what the lease remembers for it can go.
            if (IsTerminal(activity.Status))
            {
                _activities.Remove(id);
                return;
            }

            if (!_activities.TryGetValue(id, out var persisted))
                _activities[id] = persisted = new Persisted();
            foreach (var stamp in activity.Transitions ?? [])
                if (!persisted.Stamps.Contains(stamp))
                    persisted.Stamps.Add(stamp);
            foreach (var record in activity.VariableWrites ?? [])
                if (record.CommitSequence is >= 0 and var number)
                    persisted.Records.TryAdd(RecordKey(activity, record), number);
        }
    }

    private sealed class Persisted
    {
        public List<ActivityLifecycleStamp> Stamps { get; } = [];
        public Dictionary<string, long> Records { get; } = new(StringComparer.Ordinal);
    }
}
