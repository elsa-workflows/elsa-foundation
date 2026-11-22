namespace Elsa.Workflows.Runtime.Api.Models;

// Spec 1229 (S-6): the variable lifecycle inspection views. Values never appear inline: a value is a
// VariableValueStateView, and a captured one carries a payload reference for the authorized payload read (FR-006).
// The views are public contracts of the runtime API; members are added, never renamed.

/// <summary>Who made a variable write: the activity execution (node, logical invocation, attempt) or the alteration.</summary>
public sealed record VariableLifecycleWriterView(
    string Source,
    string? ExecutableNodeId,
    string? ActivityExecutionId,
    string? LogicalInvocationId,
    string? AttemptId,
    string? BranchId,
    string? ExecutionPathId,
    string? AlterationId);

/// <summary>
/// What inspection can show of a value: <c>captured</c>, <c>omitted</c> (not captured), <c>redacted</c> (captured, the
/// caller may not see it) or <c>expired</c> (removed after capture). None of them is <c>null</c>; a captured <c>null</c>
/// is a value, read through the payload reference.
/// </summary>
public sealed record VariableValueStateView(
    string State,
    string? Digest,
    VariableValuePayloadRefView? PayloadRef,
    bool IsOperational);

/// <summary>Addresses one value for the payload read: the entry and which of its values.</summary>
public sealed record VariableValuePayloadRefView(
    string OwnerKind,
    string OwnerId,
    string EntryId,
    string Value);

/// <summary>The earlier applied write a concurrent overwrite replaced (spec 1229 Q3).</summary>
public sealed record VariableOverwriteView(
    string OverwrittenEntryId,
    VariableLifecycleWriterView OverwrittenWriter,
    long? OverwrittenCommitSequence,
    bool IsLostUpdate);

/// <summary>
/// One write-history entry: a spec 1221 write record of an activity execution or an alteration. A lost update (applied,
/// refused or accepted) names <see cref="OtherWriter"/>, the writer of the version it met.
/// </summary>
public sealed record VariableWriteEntryView(
    string EntryId,
    string ScopeId,
    string? FrameId,
    string VariableKey,
    VariableLifecycleWriterView Writer,
    string Source,
    string Outcome,
    long? BaseVersion,
    long CurrentVersion,
    long NewVersion,
    long? CommitSequence,
    long FrameRevision,
    bool OrderApproximate,
    bool IsApplied,
    bool IsCurrentValue,
    VariableOverwriteView? ConcurrentOverwrite,
    VariableValueStateView Value,
    VariableValueStateView? OverwrittenValue,
    VariableValueStateView? RefusedValue,
    string? ResolvesEntryId,
    DateTimeOffset OccurredAt,
    VariableLifecycleWriterView? OtherWriter = null);

/// <summary>A page of the write history; <see cref="ConcurrentOverwriteTotal"/> is set when the history is filtered on overwrites.</summary>
public sealed record VariableWriteHistoryPageView(
    IReadOnlyList<VariableWriteEntryView> Items,
    string? Next,
    int? ConcurrentOverwriteTotal);

/// <summary>One variable instance (frame and key) with its current version, last writer and value state.</summary>
public sealed record VariableSummaryView(
    string ScopeId,
    string FrameId,
    string VariableKey,
    string FrameKind,
    long Version,
    VariableLifecycleWriterView? LastWriter,
    long? LastCommitSequence,
    VariableValueStateView Value,
    bool HistoryRecorded);

/// <summary>One activity lifecycle transition in a journal commit.</summary>
public sealed record JournalTransitionView(
    string ActivityExecutionId,
    string ExecutableNodeId,
    string RunTimePath,
    string Kind,
    string? BranchId,
    string? ExecutionPathId,
    int? Evaluation);

/// <summary>An input refresh a background admission made, with both evaluations (spec 1221 FR-020).</summary>
public sealed record JournalInputRefreshView(
    string ActivityExecutionId,
    int SupersededEvaluation,
    int ReplacedByEvaluation,
    string Reason,
    IReadOnlyList<string> ChangedVariables);

/// <summary>One commit of an instance's journal: its transitions, input refreshes and writes, in commit order.</summary>
public sealed record JournalCommitView(
    long? CommitSequence,
    bool OrderApproximate,
    DateTimeOffset? OccurredAt,
    IReadOnlyList<JournalTransitionView> Transitions,
    IReadOnlyList<JournalInputRefreshView> InputRefreshes,
    IReadOnlyList<VariableWriteEntryView> Writes);

/// <summary>A page of the journal.</summary>
public sealed record JournalPageView(
    IReadOnlyList<JournalCommitView> Items,
    string? Next);

/// <summary>Per variable instance, whether two instances ended with different values or wrote different sequences.</summary>
public sealed record VariableComparisonView(
    string FramePath,
    string VariableKey,
    bool FinalDigestDiffers,
    bool WrittenDigestsDiffer,
    int LeftWriteCount,
    int RightWriteCount,
    bool DigestUnknown = false);

/// <summary>The first commit where the matched transitions of two instances differ.</summary>
public sealed record InstanceDivergenceView(
    int CommitIndex,
    JournalCommitView? Left,
    JournalCommitView? Right);

/// <summary>The comparison of two instances of one executable version (spec 1229 US4). It carries digests only.</summary>
public sealed record InstanceComparisonView(
    string LeftWorkflowExecutionId,
    string RightWorkflowExecutionId,
    string Executable,
    bool Comparable,
    string? RefusalReason,
    InstanceDivergenceView? FirstDivergence,
    IReadOnlyList<VariableComparisonView> Variables,
    bool Partial);

/// <summary>A value released by the authorized payload read of a write-history entry.</summary>
public sealed record VariableWritePayloadView(
    string EntryId,
    string Value,
    string CaptureMode,
    System.Text.Json.JsonElement Payload);
