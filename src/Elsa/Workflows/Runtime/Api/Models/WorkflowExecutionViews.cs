using System.Collections.ObjectModel;
using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record WorkflowInstanceListView(
    IReadOnlyCollection<WorkflowInstanceSummaryView> Items,
    string? NextCursor,
    bool HasNext,
    int Count,
    int TotalCount);

public sealed record WorkflowInstanceSummaryView(
    string WorkflowExecutionId,
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    string Status,
    string RunKind,
    string? SubStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    string? ParentWorkflowExecutionId,
    string? TenantId,
    long ActivityCount,
    int IncidentCount,
    string? SourceReferenceId = null,
    string? PublicationId = null,
    string? SlotId = null,
    string? SourceKind = null,
    string? SourceId = null,
    string? SourceVersion = null)
{
    public static WorkflowInstanceSummaryView From(
        WorkflowExecutionState state,
        long activityCount = 0,
        int incidentCount = 0,
        bool canInspectSensitiveValues = false) =>
        new(
            state.WorkflowExecutionId,
            state.PinnedExecutable.ArtifactId,
            state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId,
            state.PinnedSource?.DefinitionVersionId ?? state.PinnedExecutable.DefinitionVersionId,
            state.PinnedSource?.ArtifactVersion ?? state.PinnedExecutable.ArtifactVersion,
            state.PinnedExecutable.ArtifactHash,
            state.Status.ToString(),
            state.RunKind.ToString(),
            state.SubStatus,
            state.CreatedAt,
            state.StartedAt,
            state.UpdatedAt,
            state.CompletedAt,
            canInspectSensitiveValues ? state.CorrelationId : null,
            state.ParentWorkflowExecutionId,
            state.TenantId,
            activityCount,
            incidentCount,
            state.PinnedSource?.SourceReferenceId,
            state.PinnedSource?.PublicationId,
            state.PinnedSource?.SlotId,
            state.PinnedSource?.SourceKind,
            state.PinnedSource?.SourceId,
            state.PinnedSource?.SourceVersion);
}

/// <summary>
/// Instance detail read model. Beyond the per-instance projections it also surfaces two host-configuration-derived
/// checkpoint-cadence properties (ADR 0032 R3): <paramref name="CheckpointCadence"/>
/// (<c>Immediate</c>/<c>Coalesced</c>, with <paramref name="MaxSegmentCheckpoints"/> when Coalesced) and the
/// <paramref name="InspectionGranularity"/> that cadence implies (<c>activity-level</c> vs <c>boundary-level</c>). They
/// let an inspection consumer know whether per-activity evidence is exact or folded to a coalesced boundary, rather than
/// assuming activity-level fidelity. See <see cref="Coalescing.RuntimeCheckpointCadenceInspector"/> for the host-vs-run
/// projection limitation.
/// </summary>
public sealed record WorkflowInstanceDetailsView(
    WorkflowInstanceSummaryView Instance,
    IReadOnlyCollection<ActivityExecutionInspectionSummaryView> Activities,
    IReadOnlyCollection<IncidentStateView> Incidents,
    IReadOnlyDictionary<string, WorkflowOutputView> Outputs,
    string CheckpointCadence,
    int? MaxSegmentCheckpoints,
    string InspectionGranularity,
    string? ActivityNextContinuationToken = null);

/// <summary>
/// A named workflow output on the instance details view (#254 Seam R1): the durably captured value a
/// <c>SetOutput</c> leaf assigned, projected read-only from the instance's <c>output:</c>-prefixed durable
/// values. When the configured <c>IRuntimePayloadCapturePolicy</c> declines to expose the payload (including
/// sensitive-marked values), the output surfaces as an explicit redacted marker — the name is present,
/// <see cref="IsRedacted"/> is true, <see cref="Value"/> is null, and <see cref="RedactionReason"/> carries the
/// policy's reason — never silently absent.
/// </summary>
public sealed record WorkflowOutputView(
    object? Value,
    bool IsRedacted,
    string? RedactionReason,
    DateTimeOffset CapturedAt)
{
    public static WorkflowOutputView From(WorkflowOutputProjection projection) =>
        new(projection.Value, projection.IsRedacted, projection.RedactionReason, projection.CapturedAt);

    public static WorkflowOutputView Redacted(DateTimeOffset capturedAt) =>
        new(null, true, "Caller is not authorized to inspect sensitive values.", capturedAt);
}

public sealed record ActivityExecutionInspectionView(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FirstCheckpointId,
    string? LastCheckpointId,
    DateTimeOffset? LastCommittedAt,
    ActivitySchedulingProvenanceView Provenance,
    IReadOnlyCollection<string> OutcomeNames,
    IReadOnlyCollection<ActivityExecutionBookmarkSummaryView> Bookmarks,
    IReadOnlyCollection<ActivityExecutionIncidentSummaryView> Incidents,
    IReadOnlyCollection<ActivityExecutionInspectionValueSnapshotView> ValueSnapshots,
    ActivityExecutionAttemptView? Attempt,
    ActivityExecutionBoundaryView? Boundary,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionInspectionView From(
        ActivityExecutionInspectionProjection projection,
        ActivityExecutionBoundary? boundary = null,
        bool canInspectSensitiveValues = false,
        ActivityExecutionAttemptNavigation? attemptNavigation = null,
        bool canResolveValuePayloads = false) =>
        new(
            projection.ActivityExecutionId,
            projection.WorkflowExecutionId,
            projection.ExecutableNodeId,
            projection.AuthoredActivityId,
            projection.ActivityType,
            projection.ActivityTypeVersion,
            projection.Status.ToString(),
            projection.SubStatus,
            projection.ExecutionSequence,
            projection.ScheduledAt,
            projection.StartedAt,
            projection.CompletedAt,
            projection.FirstCheckpointId,
            projection.LastCheckpointId,
            projection.LastCommittedAt,
            ActivitySchedulingProvenanceView.From(projection.Provenance, canInspectSensitiveValues),
            projection.OutcomeNames,
            projection.Bookmarks.Select(ActivityExecutionBookmarkSummaryView.From).ToArray(),
            projection.Incidents.Select(x => ActivityExecutionIncidentSummaryView.From(x, canInspectSensitiveValues)).ToArray(),
            projection.ValueSnapshots.Select((x, index) => ActivityExecutionInspectionValueSnapshotView.From(
                x,
                projection.ActivityExecutionId,
                index,
                canInspectSensitiveValues,
                canResolveValuePayloads)).ToArray(),
            ActivityExecutionAttemptView.From(attemptNavigation, projection.Attempt ?? projection.Provenance.Attempt),
            ActivityExecutionBoundaryView.From(boundary),
            ActivityExecutionInspectionDisclosure.Metadata(projection.Metadata, canInspectSensitiveValues));
}

public sealed record ActivityExecutionAttemptView(
    int AttemptNumber,
    string FirstAttemptActivityExecutionId,
    string? PreviousAttemptActivityExecutionId,
    string? NextAttemptActivityExecutionId,
    int? TotalAttempts)
{
    public static ActivityExecutionAttemptView? From(ActivityExecutionAttemptLineage? attempt) =>
        attempt is null ? null : new(
            attempt.AttemptNumber,
            attempt.FirstAttemptActivityExecutionId,
            attempt.PreviousAttemptActivityExecutionId,
            null,
            null);

    public static ActivityExecutionAttemptView? From(
        ActivityExecutionAttemptNavigation? navigation,
        ActivityExecutionAttemptLineage? fallback) =>
        navigation is null
            ? From(fallback)
            : new(
                navigation.Lineage.AttemptNumber,
                navigation.Lineage.FirstAttemptActivityExecutionId,
                navigation.Lineage.PreviousAttemptActivityExecutionId,
                navigation.NextAttemptActivityExecutionId,
                navigation.TotalAttempts);
}

public sealed record ActivityExecutionBoundaryView(
    string Kind,
    string DefinitionId,
    string DefinitionVersionId,
    string Version,
    string TemplateHash,
    IReadOnlyList<ActivityInvocationOriginSegmentView> InvocationOrigin,
    string ExecutionScopeId,
    bool HasChildren,
    int DirectChildCount,
    long CommittedDescendantCount,
    ActivityExecutionHierarchyAggregateView Aggregate,
    bool LayoutAvailable)
{
    public static ActivityExecutionBoundaryView? From(ActivityExecutionBoundary? boundary) => boundary is null ? null : new(
        boundary.Kind,
        boundary.DefinitionId,
        boundary.DefinitionVersionId,
        boundary.Version,
        boundary.TemplateHash,
        boundary.InvocationOrigin.Segments.Select(ActivityInvocationOriginSegmentView.From).ToArray(),
        boundary.ExecutionScopeId,
        boundary.HasChildren,
        boundary.DirectChildCount,
        boundary.CommittedDescendantCount,
        ActivityExecutionHierarchyAggregateView.From(boundary.Aggregate),
        boundary.LayoutAvailable);
}

public sealed record ActivityInvocationOriginSegmentView(string Kind, string Id)
{
    public static ActivityInvocationOriginSegmentView From(ActivityInvocationOriginSegment segment) =>
        new(segment.Kind.ToString(), segment.Id);
}

public sealed record ActivityExecutionHierarchyAggregateView(
    string Status,
    long Total,
    long Scheduled,
    long Running,
    long Suspended,
    long Completed,
    long Faulted,
    long Cancelled,
    long BlockingIncidentCount,
    long RetryCount,
    long LastExecutionSequence)
{
    public static ActivityExecutionHierarchyAggregateView From(ActivityExecutionHierarchyAggregate value) => new(
        value.Status.ToString(), value.Total, value.Scheduled, value.Running, value.Suspended, value.Completed,
        value.Faulted, value.Cancelled, value.BlockingIncidentCount, value.RetryCount, value.LastExecutionSequence);
}

public sealed record ActivitySchedulingProvenanceView(
    string? ParentActivityExecutionId,
    string? SchedulingActivityExecutionId,
    string? SchedulingWorkflowExecutionId,
    string? BranchId,
    string? IterationId,
    string? ExecutionPathId,
    string? ExecutionScopeId,
    string? SchedulingCause,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivitySchedulingProvenanceView From(
        ActivitySchedulingProvenance provenance,
        bool canInspectSensitiveValues = false) =>
        new(
            provenance.ParentActivityExecutionId,
            provenance.SchedulingActivityExecutionId,
            provenance.SchedulingWorkflowExecutionId,
            provenance.BranchId,
            provenance.IterationId,
            provenance.ExecutionPathId,
            provenance.ExecutionScopeId,
            provenance.SchedulingCause,
            ActivityExecutionInspectionDisclosure.Metadata(provenance.Metadata, canInspectSensitiveValues));
}

public sealed record ActivityExecutionBookmarkSummaryView(
    ActivityExecutionBookmarkIdentityView Identity,
    string StimulusType,
    string StimulusHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    public static ActivityExecutionBookmarkSummaryView From(ActivityExecutionBookmarkSummary summary) =>
        new(
            new(
                summary.Identity.BookmarkId,
                summary.Identity.WorkflowExecutionId,
                summary.Identity.ActivityExecutionId,
                summary.Identity.ResumeTargetId),
            summary.StimulusType,
            summary.StimulusHash,
            summary.CreatedAt,
            summary.ExpiresAt);
}

public sealed record ActivityExecutionBookmarkIdentityView(
    string BookmarkId,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ResumeTargetId);

public sealed record ActivityExecutionIncidentSummaryView(
    string IncidentId,
    string Severity,
    string Status,
    IncidentResolutionOutcomeView? ResolutionOutcome,
    string FailureType,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool IsBlocking,
    ActivityExecutionIncidentCausationView? Causation,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionIncidentSummaryView From(
        ActivityExecutionIncidentSummary summary,
        bool canInspectSensitiveValues = false) =>
        new(
            summary.IncidentId,
            summary.Severity.ToString(),
            summary.Status.ToString(),
            IncidentResolutionOutcomeView.From(summary.ResolutionOutcome),
            summary.FailureType,
            canInspectSensitiveValues ? summary.Message : "Incident details are redacted.",
            summary.CreatedAt,
            summary.ResolvedAt,
            summary.IsBlocking,
            ActivityExecutionIncidentCausationView.From(summary.Causation),
            ActivityExecutionInspectionDisclosure.Metadata(summary.Metadata, canInspectSensitiveValues));
}

public sealed record ActivityExecutionIncidentCausationView(
    string IncidentId,
    string ActivityExecutionId,
    string? ExecutableNodeId,
    string Kind)
{
    public static ActivityExecutionIncidentCausationView? From(ActivityExecutionIncidentCausation? causation) =>
        causation is null
            ? null
            : new(causation.IncidentId, causation.ActivityExecutionId, causation.ExecutableNodeId, causation.Kind);
}

public sealed record ActivityExecutionInspectionValueSnapshotView(
    string EvidenceId,
    string Name,
    string Subject,
    string CaptureMode,
    string CaptureState,
    RuntimeValueTypeDescriptor? Type,
    DateTimeOffset CapturedAt,
    object? Payload,
    object? Snapshot,
    string CaptureReason,
    bool IsSensitive,
    IReadOnlyDictionary<string, string> Metadata,
    string? InputKey = null,
    string? EvaluationId = null,
    string? EvaluationPhase = null,
    long? EvaluationSequence = null,
    string? AccessState = null,
    RuntimeInputEvaluationFailure? Failure = null)
{
    public static ActivityExecutionInspectionValueSnapshotView From(ActivityExecutionInspectionValueSnapshot snapshot, bool canInspectSensitiveValues = false) =>
        From(snapshot, "unknown", 0, canInspectSensitiveValues, false);

    public static ActivityExecutionInspectionValueSnapshotView From(
        ActivityExecutionInspectionValueSnapshot snapshot,
        string activityExecutionId,
        int ordinal,
        bool canInspectSensitiveValues,
        bool canResolveValuePayloads) =>
        new(
            snapshot.EvidenceId ?? ActivityExecutionValueEvidenceIdentity.Create(activityExecutionId, snapshot, ordinal),
            snapshot.Name,
            snapshot.Subject.ToString(),
            snapshot.CaptureMode.ToString(),
            DetermineCaptureState(snapshot),
            snapshot.Type,
            snapshot.CapturedAt,
            null,
            null,
            snapshot.CaptureReason,
            snapshot.IsSensitive,
            ActivityExecutionInspectionDisclosure.Metadata(snapshot.Metadata, canInspectSensitiveValues),
            snapshot.InputKey,
            snapshot.EvaluationId,
            snapshot.Phase,
            snapshot.Sequence,
            DetermineAccessState(snapshot, canInspectSensitiveValues, canResolveValuePayloads),
            canInspectSensitiveValues ? snapshot.Failure : null);

    private static string DetermineCaptureState(ActivityExecutionInspectionValueSnapshot snapshot) =>
        snapshot.Failure is not null
            ? "captureFailed"
            : snapshot.CaptureMode switch
        {
            RuntimePayloadCaptureMode.None => "notCaptured",
            RuntimePayloadCaptureMode.MetadataOnly => "metadataOnly",
            RuntimePayloadCaptureMode.DiagnosticSnapshot when snapshot.Payload is not null => "diagnosticSnapshotCaptured",
            RuntimePayloadCaptureMode.Payload when snapshot.Payload is not null => "payloadCaptured",
            _ => "unavailable"
        };

    private static string DetermineAccessState(
        ActivityExecutionInspectionValueSnapshot snapshot,
        bool canInspectSensitiveValues,
        bool canResolveValuePayloads)
    {
        if (snapshot.Payload is null ||
            snapshot.CaptureMode is not (RuntimePayloadCaptureMode.DiagnosticSnapshot or RuntimePayloadCaptureMode.Payload))
            return "unavailable";
        if (!canInspectSensitiveValues)
            return "redacted";
        return canResolveValuePayloads ? "resolutionAvailable" : "resolutionPermissionRequired";
    }
}

public sealed record ActivityExecutionInspectionSummaryView(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FirstCheckpointId,
    string? LastCheckpointId,
    DateTimeOffset? LastCommittedAt,
    ActivitySchedulingProvenanceView Provenance,
    IReadOnlyCollection<string> OutcomeNames,
    int BookmarkCount,
    int IncidentCount,
    int ValueSnapshotCount,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionInspectionSummaryView From(ActivityExecutionInspectionSummaryProjection projection) =>
        new(
            projection.ActivityExecutionId,
            projection.WorkflowExecutionId,
            projection.ExecutableNodeId,
            projection.AuthoredActivityId,
            projection.ActivityType,
            projection.ActivityTypeVersion,
            projection.Status.ToString(),
            projection.SubStatus,
            projection.ExecutionSequence,
            projection.ScheduledAt,
            projection.StartedAt,
            projection.CompletedAt,
            projection.FirstCheckpointId,
            projection.LastCheckpointId,
            projection.LastCommittedAt,
            ActivitySchedulingProvenanceView.From(projection.Provenance, canInspectSensitiveValues: false),
            projection.OutcomeNames,
            projection.BookmarkCount,
            projection.IncidentCount,
            projection.ValueSnapshotCount,
            ActivityExecutionInspectionDisclosure.EmptyMetadata);
}

public sealed record ActivityExecutionStateView(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    string Status,
    string? SubStatus,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? SchedulingActivityExecutionId,
    string? ParentActivityExecutionId,
    string? BranchId,
    string? IterationId,
    int? CallStackDepth,
    IReadOnlyCollection<string> BookmarkIds,
    IReadOnlyCollection<string> IncidentIds,
    int FaultCount,
    int AggregateFaultCount,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionStateView From(ActivityExecutionState state) =>
        new(
            state.Execution.ActivityExecutionId,
            state.Execution.WorkflowExecutionId,
            state.Execution.ExecutableNodeId,
            state.Execution.AuthoredActivityId,
            state.Execution.ActivityType,
            state.Execution.ActivityTypeVersion,
            state.Status.ToString(),
            state.SubStatus,
            state.ScheduledAt,
            state.StartedAt,
            state.CompletedAt,
            state.SchedulingActivityExecutionId,
            state.ParentActivityExecutionId,
            state.BranchId,
            state.IterationId,
            state.CallStackDepth,
            state.BookmarkIds,
            state.IncidentIds,
            state.FaultCount,
            state.AggregateFaultCount,
            ActivityExecutionInspectionDisclosure.EmptyMetadata);
}

public sealed record IncidentStateView(
    string IncidentId,
    string WorkflowExecutionId,
    string? ActivityExecutionId,
    string? ExecutableNodeId,
    string Severity,
    string Status,
    IncidentResolutionOutcomeView? ResolutionOutcome,
    string FailureType,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool IsBlocking,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static IncidentStateView From(IncidentState state, bool canInspectSensitiveValues = false) =>
        new(
            state.IncidentId,
            state.WorkflowExecutionId,
            state.ActivityExecutionId,
            state.ExecutableNodeId,
            state.Severity.ToString(),
            state.Status.ToString(),
            IncidentResolutionOutcomeView.From(state.ResolutionOutcome),
            state.FailureType,
            canInspectSensitiveValues ? state.Message : "Incident details are redacted.",
            state.CreatedAt,
            state.ResolvedAt,
            state.IsBlocking,
            ActivityExecutionInspectionDisclosure.Metadata(state.Metadata, canInspectSensitiveValues));
}

public sealed record IncidentStrategyReferenceView(string Alias, string Version);

public sealed record IncidentResolutionOutcomeView(
    string ActionKind,
    DateTimeOffset AppliedAt,
    IncidentStrategyReferenceView? Strategy,
    string? SystemSource,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static IncidentResolutionOutcomeView? From(IncidentResolutionOutcome? outcome) =>
        outcome is null
            ? null
            : new(
                outcome.ActionKind,
                outcome.AppliedAt,
                outcome.Strategy is null
                    ? null
                    : new IncidentStrategyReferenceView(outcome.Strategy.Alias, outcome.Strategy.Version),
                outcome.SystemSource,
                outcome.Metadata);
}

internal static class ActivityExecutionInspectionDisclosure
{
    public static IReadOnlyDictionary<string, string> EmptyMetadata { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, string> Metadata(
        IReadOnlyDictionary<string, string> metadata,
        bool canInspectSensitiveValues) =>
        canInspectSensitiveValues ? metadata : EmptyMetadata;
}

public sealed record WorkflowExecutionStartDispatchView(
    string WorkflowExecutionId,
    string ArtifactId,
    string ArtifactVersion,
    string ArtifactHash,
    string CommandDispatchStatus,
    string EnvelopeId,
    string AgentId,
    string AgentProviderName,
    string? Reason,
    string? SourceReferenceId = null,
    string? PublicationId = null,
    string? SlotId = null,
    bool Shed = false,
    int? RetryAfterSeconds = null)
{
    public static WorkflowExecutionStartDispatchView From(WorkflowExecutionStartDispatchResult result) =>
        new(
            result.WorkflowExecutionId,
            result.PinnedExecutable.ArtifactId,
            result.PinnedSource?.ArtifactVersion ?? result.PinnedExecutable.ArtifactVersion,
            result.PinnedExecutable.ArtifactHash,
            result.CommandDispatch.Status.ToString(),
            result.CommandDispatch.EnvelopeId,
            result.Agent.AgentId,
            result.Agent.ProviderName,
            result.CommandDispatch.Reason,
            result.PinnedSource?.SourceReferenceId,
            result.PinnedSource?.PublicationId,
            result.PinnedSource?.SlotId,
            // Deferred alone does not identify backpressure — the distributed leaf returns it for "forwarded to the
            // owning node" — so the shed marker is lifted out of the dispatch metadata rather than inferred from the
            // status. The endpoint needs it to choose 429 over 200 (RB1, #1235).
            IsShed(result.CommandDispatch.Metadata),
            ReadRetryAfterSeconds(result.CommandDispatch.Metadata));

    private static bool IsShed(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(RuntimeMetadataKeys.DispatchShed, out var shed) &&
        string.Equals(shed, "true", StringComparison.Ordinal);

    private static int? ReadRetryAfterSeconds(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(RuntimeMetadataKeys.DispatchRetryAfterSeconds, out var seconds) &&
        int.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
