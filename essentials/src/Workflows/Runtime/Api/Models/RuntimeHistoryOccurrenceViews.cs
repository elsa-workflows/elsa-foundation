using System.Globalization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>All 64-bit ordering identities are decimal strings; clients must not round them.</summary>
public sealed record RuntimeHistoryOccurrenceCoverageView(string State, string? Limitation, string? PolicyId,
    int? PolicyVersion, IReadOnlyList<string> SupportedKinds, bool CapturedFromStart, bool HasGaps,
    string FirstSequence, string LastSequence, string SettledSequence, string PurgedThroughSequence,
    string? LastPhysicalCommitId, DateTimeOffset? LastCommittedAt, bool? CaptureAvailable,
    string? CaptureLimitation, bool? SourceHistoryPending, string? SourceHistoryLimitation)
{
    internal static RuntimeHistoryOccurrenceCoverageView From(RuntimeHistoryCoverageView value) => new(
        value.State, value.Limitation, value.PolicyId, value.PolicyVersion, value.SupportedKinds,
        value.CapturedFromStart, value.HasGaps, Decimal(value.FirstSequence), Decimal(value.LastSequence),
        Decimal(value.SettledSequence), Decimal(value.PurgedThroughSequence), value.LastPhysicalCommitId,
        value.LastCommittedAt, value.CaptureAvailable, value.CaptureLimitation, value.SourceHistoryPending, value.SourceHistoryLimitation);

    internal static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed record RuntimeHistoryOccurrenceView(string Position, string WorkflowExecutionId,
    string ActivityExecutionId, string HistoryEventId, string Sequence, string ObservationKind,
    DateTimeOffset? OccurredAt, bool? OccurredAtKnown, DateTimeOffset? RecordedAt, string? ExecutableNodeId,
    string? ParentActivityExecutionId, bool? ActivityAncestryKnown, string? ExecutionScopeId,
    string? IterationId, string? AttemptId, RuntimeHistoryCausation? Causation,
    string? PreviousActivityExecutionId, string? NextActivityExecutionId)
{
    internal static RuntimeHistoryOccurrenceView From(RuntimeHistoryOccurrence entry)
    {
        var item = entry.Observation;
        return new(RuntimeHistoryOccurrenceCoverageView.Decimal(entry.Position), item.WorkflowExecutionId,
            item.ActivityExecutionId!, item.HistoryEventId, RuntimeHistoryOccurrenceCoverageView.Decimal(item.Sequence),
            item.Name, item.OccurredAtKnown == false ? null : item.OccurredAt, item.OccurredAtKnown,
            item.RecordedAt, item.ExecutableNodeId, item.ParentActivityExecutionId, item.ActivityAncestryKnown,
            item.ExecutionScopeId, item.IterationId, item.AttemptId, item.Causation,
            entry.PreviousActivityExecutionId, entry.NextActivityExecutionId);
    }
}

public sealed record RuntimeHistoryOccurrencePageView(string State, string? Reason,
    RuntimeHistoryOccurrenceCoverageView Coverage, string UpperSequence, string RetainedAfterSequence,
    string? TotalCount, string? SelectedPosition, IReadOnlyList<RuntimeHistoryOccurrenceView> Items,
    string? SequenceToken, bool HasNewerHistory, DateTimeOffset ReadAt);
