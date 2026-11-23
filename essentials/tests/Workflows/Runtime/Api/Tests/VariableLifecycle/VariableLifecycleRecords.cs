using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle;

/// <summary>Builds the committed record sets spec 1229 reads: rows with lifecycle stamps, write records and provenance.</summary>
internal static class VariableLifecycleRecords
{
    public const string WorkflowExecutionId = "wfexec-1";
    public const string RootFrame = "frame-root";
    public const string RootScope = "scope-root";
    public static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public static ActivityExecutionState Row(
        string id,
        string node,
        string? parent = null,
        string? scheduler = null,
        IReadOnlyList<string>? causedBy = null,
        IReadOnlyList<(ActivityLifecycleTransitionKind Kind, long Commit)>? stamps = null,
        IReadOnlyList<VariableWriteRecord>? writes = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivityExecutionAttemptLineage? attempt = null,
        long executionSequence = 0,
        string? branchId = null,
        ActivityExecutionStatus status = ActivityExecutionStatus.Completed)
    {
        var transitions = stamps?.Select(stamp => new ActivityLifecycleStamp(stamp.Kind, stamp.Commit, branchId)).ToArray();
        var numbers = (transitions ?? []).Select(stamp => stamp.CommitSequence)
            .Concat((writes ?? []).Where(record => record.CommitSequence is >= 0).Select(record => record.CommitSequence!.Value))
            .ToArray();
        var provenance = ActivitySchedulingProvenance.From(WorkflowExecutionId, parent, scheduler ?? parent, branchId, null, null, null, null, attempt: attempt)
            with { CausedBy = causedBy };
        return new ActivityExecutionState(
            new ActivityExecution(id, WorkflowExecutionId, node, $"authored-{node}", "Test", "1.0.0"),
            status,
            null,
            executionSequence,
            Now,
            Now,
            Now,
            scheduler ?? parent,
            parent,
            branchId,
            null,
            provenance,
            null,
            [],
            [],
            0,
            0,
            metadata ?? new Dictionary<string, string>(),
            Attempt: attempt)
        {
            VariableWrites = writes,
            Transitions = transitions,
            FirstCommitSequence = numbers.Length == 0 ? null : numbers.Min(),
            LastCommitSequence = numbers.Length == 0 ? null : numbers.Max()
        };
    }

    public static VariableWriteRecord Write(
        string writerId,
        string node,
        long? commit,
        long current,
        long next,
        VariableCommitOutcome outcome = VariableCommitOutcome.Blind,
        long? baseVersion = null,
        string key = "count",
        string? frame = RootFrame,
        long frameRevision = 0,
        VariableWriteSource source = VariableWriteSource.Capture,
        string? digest = null,
        ValueEnvelope? overwritten = null,
        ValueEnvelope? rejected = null) =>
        new(RootScope, key, new VariableWriter(source, node, writerId), outcome, baseVersion, current, next, frameRevision == 0 ? next : frameRevision, Now.AddSeconds(commit ?? 0),
            outcome is VariableCommitOutcome.LostUpdateWarning or VariableCommitOutcome.LostUpdateFault ? new VariableWriter(VariableWriteSource.Capture, "other", "other") : null,
            overwritten)
        {
            CommitSequence = commit,
            FrameId = frame,
            Digest = digest ?? (outcome == VariableCommitOutcome.LostUpdateFault ? null : $"sha256:{key}-{next}"),
            RejectedValue = rejected
        };

    public static (ActivityLifecycleTransitionKind, long) Scheduled(long commit) => (ActivityLifecycleTransitionKind.Scheduled, commit);
    public static (ActivityLifecycleTransitionKind, long) Started(long commit) => (ActivityLifecycleTransitionKind.Started, commit);
    public static (ActivityLifecycleTransitionKind, long) Suspended(long commit) => (ActivityLifecycleTransitionKind.Suspended, commit);
    public static (ActivityLifecycleTransitionKind, long) Resumed(long commit) => (ActivityLifecycleTransitionKind.Resumed, commit);
    public static (ActivityLifecycleTransitionKind, long) Completed(long commit) => (ActivityLifecycleTransitionKind.Completed, commit);
    public static (ActivityLifecycleTransitionKind, long) Faulted(long commit) => (ActivityLifecycleTransitionKind.Faulted, commit);

    public static WorkflowAlterationJobState AlterationJob(string jobId, params VariableWriteRecord[] writes) =>
        new(jobId, "plan-1", WorkflowExecutionId, "tenant", 0, WorkflowAlterationJobStatus.Succeeded, null, 1,
            [new WorkflowAlterationOutcome(0, "ModifyVariable", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Applied", null, Now) { VariableWrites = writes }],
            null, null, Now, Now, Now, 1);

    public static IReadOnlyDictionary<string, string> RetryOf(string faultedId) =>
        new Dictionary<string, string> { [RuntimeMetadataKeys.RetryOfActivityExecutionId] = faultedId };
}
