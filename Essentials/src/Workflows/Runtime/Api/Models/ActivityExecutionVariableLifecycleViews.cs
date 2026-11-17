using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>
/// Where a value of the variable lifecycle stands (spec 1221, spec 1229 FR-006): <c>captured</c>, <c>omitted</c>,
/// <c>redacted</c> or <c>expired</c>. A view never carries the value itself, so a captured <c>null</c> reads as
/// <c>captured</c>, never as a missing value. <see cref="EvidenceId"/> names the activity value evidence whose
/// authorized payload read returns the value, when there is one; operational values (an overwritten or refused value)
/// become readable through the write-history payload read of spec 1229. This view produces <c>captured</c>,
/// <c>omitted</c> and <c>redacted</c>; evidence removed by retention has no snapshot left and reads as <c>omitted</c>.
/// <c>expired</c> belongs to the shared vocabulary of spec 1229, whose history read can tell it apart.
/// </summary>
public sealed record VariableLifecycleValueStateView(
    string State,
    string? Digest = null,
    string? EvidenceId = null,
    bool IsOperational = false)
{
    public const string Captured = "captured";
    public const string Omitted = "omitted";
    public const string Redacted = "redacted";
    public const string Expired = "expired";

    /// <summary>The state of a value kept on a durable record: never the value, only whether it is there.</summary>
    public static VariableLifecycleValueStateView FromEnvelope(ValueEnvelope? value, bool isOperational = false) =>
        new(value switch
        {
            null => Omitted,
            { Presence: ValuePresence.Withheld } => Redacted,
            { Presence: ValuePresence.Absent } => Omitted,
            _ when value.Policy.HidesValue() => Redacted,
            _ => Captured
        }, IsOperational: isOperational);

    /// <summary>The state of a value the payload capture policy recorded as activity value evidence.</summary>
    public static VariableLifecycleValueStateView FromEvidence(ActivityExecutionInspectionValueSnapshotView? evidence, string? digest)
    {
        if (evidence is null)
            return new(Omitted, digest);
        var state = evidence.AccessState switch
        {
            "redacted" => Redacted,
            "resolutionAvailable" or "resolutionPermissionRequired" => Captured,
            _ when evidence.IsSensitive => Redacted,
            _ => Omitted
        };
        return new(state, digest, state == Captured ? evidence.EvidenceId : null);
    }
}

/// <summary>Who made a variable write (spec 1221 FR-024).</summary>
public sealed record VariableWriterView(string Source, string? ExecutableNodeId, string? InvocationId, string? AttemptId)
{
    public static VariableWriterView? From(VariableWriter? writer) =>
        writer is null ? null : new(writer.Source.ToString(), writer.ExecutableNodeId, writer.InvocationId, writer.AttemptId);
}

/// <summary>
/// One write-history record of this activity execution (spec 1221 FR-024–FR-027): the metadata and, for each value,
/// only its state. <c>Outcome</c> is <c>LostUpdateWarning</c> when the write applied over a newer version, and
/// <c>LostUpdateFault</c> when a <c>Fault</c> node's write was refused.
/// </summary>
public sealed record VariableWriteRecordView(
    string ScopeId,
    string? FrameId,
    string VariableKey,
    VariableWriterView Writer,
    string Outcome,
    long? BaseVersion,
    long CurrentVersion,
    long NewVersion,
    long CommitOrder,
    long? CommitSequence,
    DateTimeOffset OccurredAt,
    VariableWriterView? OverwrittenWriter,
    VariableLifecycleValueStateView? WrittenValue,
    VariableLifecycleValueStateView? OverwrittenValue,
    VariableLifecycleValueStateView? RejectedValue)
{
    public static VariableWriteRecordView From(VariableWriteRecord record, IReadOnlyCollection<ActivityExecutionInspectionValueSnapshotView> evidence)
    {
        var isLostUpdate = record.Outcome is VariableCommitOutcome.LostUpdateWarning or VariableCommitOutcome.LostUpdateFault;
        return new(
            record.ScopeId,
            record.FrameId,
            record.VariableKey,
            VariableWriterView.From(record.Writer)!,
            record.Outcome.ToString(),
            record.BaseVersion,
            record.CurrentVersion,
            record.NewVersion,
            record.CommitOrder,
            record.CommitSequence is >= 0 ? record.CommitSequence : null,
            record.OccurredAt,
            VariableWriterView.From(record.OverwrittenWriter),
            record.Outcome == VariableCommitOutcome.LostUpdateFault ? null : VariableLifecycleValueStateView.FromEvidence(WrittenEvidence(record, evidence), record.Digest),
            isLostUpdate && record.OverwrittenValue is not null ? VariableLifecycleValueStateView.FromEnvelope(record.OverwrittenValue, isOperational: true) : null,
            record.Outcome == VariableCommitOutcome.LostUpdateFault ? VariableLifecycleValueStateView.FromEnvelope(record.RejectedValue, isOperational: true) : null);
    }

    // The value a write wrote is the activity's VariableWrite evidence named by the variable, its scope and the version
    // the write produced (VariableWriteValueSnapshots).
    private static ActivityExecutionInspectionValueSnapshotView? WrittenEvidence(
        VariableWriteRecord record,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshotView> evidence) =>
        evidence.LastOrDefault(item =>
            item.Subject == nameof(ActivityExecutionInspectionValueSubject.VariableWrite) &&
            StringComparer.Ordinal.Equals(item.Name, record.VariableKey) &&
            StringComparer.Ordinal.Equals(item.EvaluationPhase, record.ScopeId) &&
            item.EvaluationSequence == record.NewVersion);
}

/// <summary>
/// A successful completion a <c>Fault</c> lost update refused (spec 1221 FR-017). Its presence is what makes
/// <c>AcceptRecordedCompletion/1</c> available for the faulted activity.
/// </summary>
public sealed record RecordedCompletionView(
    string SelectionState,
    IReadOnlyCollection<string> OutcomeNames,
    string AttemptId,
    int? AttemptOrdinal,
    IReadOnlyCollection<RecordedVariableWriteView> VariableWrites)
{
    public static RecordedCompletionView? From(RecordedActivityCompletion? recorded) =>
        recorded is null
            ? null
            : new(
                recorded.Completion.Selection.State.ToString(),
                recorded.Completion.Selection.ResolveNames().ToArray(),
                recorded.Completion.AttemptId,
                recorded.Attempt?.Ordinal,
                recorded.VariableWrites
                    .Select(write => new RecordedVariableWriteView(write.VariableKey, VariableLifecycleValueStateView.FromEnvelope(write.Value, isOperational: true)))
                    .ToArray());
}

/// <summary>One variable write a recorded completion would make, with the state of the value it would write.</summary>
public sealed record RecordedVariableWriteView(string VariableKey, VariableLifecycleValueStateView Value);

/// <summary>
/// One background admission refresh (spec 1221 FR-018, FR-020): the evaluation it superseded, the one that replaced it,
/// the variables that changed in between, and the state of each superseded input value.
/// </summary>
public sealed record ActivityInputRefreshView(
    int SupersededEvaluation,
    int ReplacedByEvaluation,
    DateTimeOffset SupersededMaterializedAt,
    DateTimeOffset RefreshedAt,
    IReadOnlyCollection<VariableInputChangeView> Changes,
    IReadOnlyCollection<SupersededInputView> SupersededInputs)
{
    public static ActivityInputRefreshView From(ActivityInputRefresh refresh) =>
        new(
            refresh.Superseded.EvaluationNumber,
            refresh.ReplacedByEvaluation,
            refresh.Superseded.MaterializedAt,
            refresh.RefreshedAt,
            refresh.Changes.Select(change => new VariableInputChangeView(change.ScopeId, change.VariableKey, change.ReadVersion, change.CurrentVersion)).ToArray(),
            refresh.Superseded.Values
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new SupersededInputView(item.Key, VariableLifecycleValueStateView.FromEnvelope(item.Value)))
                .ToArray());
}

/// <summary>A variable the superseded evaluation read that changed before admission: the version read and the current one.</summary>
public sealed record VariableInputChangeView(string ScopeId, string VariableKey, long ReadVersion, long CurrentVersion);

/// <summary>One input of a superseded evaluation, by key, with the state of its value.</summary>
public sealed record SupersededInputView(string InputKey, VariableLifecycleValueStateView Value);
