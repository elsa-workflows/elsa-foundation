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
/// The number is the lease's fencing token (issued one higher on every acquisition) followed by the commit's ordinal
/// within the lease, so it grows across owners without a stored counter, and costs no read or write. A handler may
/// commit a state it built from an earlier, unstamped copy within the same lease; the stamps this lease already made
/// are merged back in by identity, so a stamp is never lost or renumbered. The same commit submitted twice gets the
/// same number, so a replay keeps its fingerprint. A commit outside a lease is not numbered.
/// </remarks>
internal static class RuntimeCommitSequenceStamper
{
    private static readonly ConditionalWeakTable<RuntimeExecutionLease, LeaseSequence> Leases = new();

    public static RuntimeCheckpointCommit Stamp(RuntimeCheckpointCommit commit, RuntimeExecutionLease? lease)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (lease is null || !StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return commit;

        var activities = commit.StateChanges.ActivityExecutions;
        var job = commit.StateChanges.AlterationJobTerminalChange;
        if (activities.Count == 0 && job?.Outcomes.All(outcome => outcome.VariableWrites is null) != false)
            return commit;

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
                        : outcome with { VariableWrites = outcome.VariableWrites.Select(record => record.CommitSequence is null ? record with { CommitSequence = sequence } : record).ToArray() }).ToArray(),
                    job.CheckpointCommitId,
                    job.CompletedAt,
                    job.SafeFailure));
            return commit with { StateChanges = changes };
        }
    }

    /// <summary>The transition an activity's status stands for, given the transition before it.</summary>
    public static ActivityLifecycleTransitionKind? TransitionOf(ActivityExecutionStatus status, ActivityLifecycleTransitionKind? previous) => status switch
    {
        ActivityExecutionStatus.Running => previous == ActivityLifecycleTransitionKind.Suspended
            ? ActivityLifecycleTransitionKind.Resumed
            : previous is ActivityLifecycleTransitionKind.Started or ActivityLifecycleTransitionKind.Resumed ? previous : ActivityLifecycleTransitionKind.Started,
        ActivityExecutionStatus.Suspended or ActivityExecutionStatus.Waiting => ActivityLifecycleTransitionKind.Suspended,
        ActivityExecutionStatus.Completed => ActivityLifecycleTransitionKind.Completed,
        ActivityExecutionStatus.Faulted => ActivityLifecycleTransitionKind.Faulted,
        ActivityExecutionStatus.Cancelled => ActivityLifecycleTransitionKind.Cancelled,
        ActivityExecutionStatus.Superseded => ActivityLifecycleTransitionKind.Superseded,
        _ => null
    };

    private sealed class LeaseSequence
    {
        private long _ordinal;
        private readonly Dictionary<string, long> _commits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _records = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ActivityLifecycleStamp>> _transitions = new(StringComparer.Ordinal);

        public long SequenceFor(string commitId, long fencingToken)
        {
            if (_commits.TryGetValue(commitId, out var known))
                return known;
            var sequence = RuntimeCommitSequence.Compose(fencingToken, ++_ordinal);
            _commits[commitId] = sequence;
            return sequence;
        }

        public ActivityExecutionState StampActivity(ActivityExecutionState activity, long sequence)
        {
            var records = activity.VariableWrites?.Select(record => StampRecord(activity, record, sequence)).ToArray();

            // Stamps this lease made on an earlier copy come back, by kind and sequence, before the status is judged.
            var stamps = (activity.Transitions ?? []).ToList();
            if (_transitions.TryGetValue(activity.Execution.ActivityExecutionId, out var made))
                foreach (var stamp in made.Where(stamp => !stamps.Any(existing => existing.Kind == stamp.Kind && existing.CommitSequence == stamp.CommitSequence)))
                    stamps.Add(stamp);
            stamps.Sort((left, right) => left.CommitSequence.CompareTo(right.CommitSequence));

            var previous = stamps.Count == 0 ? (ActivityLifecycleTransitionKind?)null : stamps[^1].Kind;
            if (TransitionOf(activity.Status, previous) is { } kind && kind != previous && stamps.All(stamp => stamp.CommitSequence != sequence || stamp.Kind != kind))
            {
                var evaluation = activity.InputSnapshot?.EvaluationNumber is > 1 and var number ? number : (int?)null;
                var stamp = new ActivityLifecycleStamp(kind, sequence, activity.BranchId, activity.Provenance.ExecutionPathId, evaluation);
                stamps.Add(stamp);
                if (!_transitions.TryGetValue(activity.Execution.ActivityExecutionId, out made))
                    _transitions[activity.Execution.ActivityExecutionId] = made = [];
                made.Add(stamp);
            }

            return activity with
            {
                VariableWrites = records ?? activity.VariableWrites,
                Transitions = stamps.Count == 0 ? activity.Transitions : stamps
            };
        }

        // A record keeps the number of the commit that first carried it in this lease; one already numbered keeps its own.
        private VariableWriteRecord StampRecord(ActivityExecutionState activity, VariableWriteRecord record, long sequence)
        {
            if (record.CommitSequence is not null)
                return record;
            var key = string.Join('\u001f', activity.Execution.ActivityExecutionId, record.ScopeId, record.VariableKey,
                record.Writer.InvocationId, record.NewVersion, record.CommitOrder, record.Outcome, record.OccurredAt.UtcTicks);
            if (!_records.TryGetValue(key, out var number))
                _records[key] = number = sequence;
            return record with { CommitSequence = number };
        }
    }
}
