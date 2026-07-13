using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record WorkflowInstanceListView(
    IReadOnlyCollection<WorkflowInstanceSummaryView> Items,
    string? PreviousCursor,
    string? NextCursor,
    bool HasPrevious,
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
    int ActivityCount,
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
        int activityCount = 0,
        int incidentCount = 0) =>
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
            state.CorrelationId,
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

public sealed record WorkflowInstanceDetailsView(
    WorkflowInstanceSummaryView Instance,
    IReadOnlyCollection<ActivityExecutionInspectionSummaryView> Activities,
    IReadOnlyCollection<IncidentStateView> Incidents,
    IReadOnlyDictionary<string, WorkflowOutputView> Outputs);

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
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionInspectionView From(ActivityExecutionInspectionProjection projection) =>
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
            ActivitySchedulingProvenanceView.From(projection.Provenance),
            projection.OutcomeNames,
            projection.Bookmarks.Select(ActivityExecutionBookmarkSummaryView.From).ToArray(),
            projection.Incidents.Select(ActivityExecutionIncidentSummaryView.From).ToArray(),
            projection.ValueSnapshots.Select(ActivityExecutionInspectionValueSnapshotView.From).ToArray(),
            projection.Metadata);
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
    public static ActivitySchedulingProvenanceView From(ActivitySchedulingProvenance provenance) =>
        new(
            provenance.ParentActivityExecutionId,
            provenance.SchedulingActivityExecutionId,
            provenance.SchedulingWorkflowExecutionId,
            provenance.BranchId,
            provenance.IterationId,
            provenance.ExecutionPathId,
            provenance.ExecutionScopeId,
            provenance.SchedulingCause,
            provenance.Metadata);
}

public sealed record ActivityExecutionBookmarkSummaryView(
    string BookmarkId,
    string ResumeTargetId,
    string StimulusType,
    string StimulusHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata,
    object? Payload)
{
    public static ActivityExecutionBookmarkSummaryView From(ActivityExecutionBookmarkSummary summary) =>
        new(
            summary.BookmarkId,
            summary.ResumeTargetId,
            summary.StimulusType,
            summary.StimulusHash,
            summary.CreatedAt,
            summary.ExpiresAt,
            summary.Metadata,
            summary.Payload);
}

public sealed record ActivityExecutionIncidentSummaryView(
    string IncidentId,
    string Severity,
    string Status,
    string ResolutionAction,
    string FailureType,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool IsBlocking,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionIncidentSummaryView From(ActivityExecutionIncidentSummary summary) =>
        new(
            summary.IncidentId,
            summary.Severity.ToString(),
            summary.Status.ToString(),
            summary.ResolutionAction.ToString(),
            summary.FailureType,
            summary.Message,
            summary.CreatedAt,
            summary.ResolvedAt,
            summary.IsBlocking,
            summary.Metadata);
}

public sealed record ActivityExecutionInspectionValueSnapshotView(
    string Name,
    string Subject,
    string CaptureMode,
    string State,
    RuntimeValueTypeDescriptor? Type,
    DateTimeOffset CapturedAt,
    object? Payload,
    object? Snapshot,
    string CaptureReason,
    bool IsSensitive,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionInspectionValueSnapshotView From(ActivityExecutionInspectionValueSnapshot snapshot) =>
        new(
            snapshot.Name,
            snapshot.Subject.ToString(),
            snapshot.CaptureMode.ToString(),
            SnapshotState(snapshot),
            snapshot.Type,
            snapshot.CapturedAt,
            snapshot.CaptureMode == RuntimePayloadCaptureMode.Payload ? snapshot.Payload : null,
            snapshot.CaptureMode == RuntimePayloadCaptureMode.DiagnosticSnapshot ? snapshot.Payload : null,
            snapshot.CaptureReason,
            snapshot.IsSensitive,
            snapshot.Metadata);

    private static string SnapshotState(ActivityExecutionInspectionValueSnapshot snapshot) =>
        snapshot.CaptureMode switch
        {
            RuntimePayloadCaptureMode.None => "notCaptured",
            RuntimePayloadCaptureMode.MetadataOnly => "metadataOnly",
            RuntimePayloadCaptureMode.DiagnosticSnapshot or RuntimePayloadCaptureMode.Payload when snapshot.Payload is not null => "captured",
            _ => "unavailable"
        };
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
            ActivitySchedulingProvenanceView.From(projection.Provenance),
            projection.OutcomeNames,
            projection.BookmarkCount,
            projection.IncidentCount,
            projection.ValueSnapshotCount,
            projection.Metadata);
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
            state.Metadata);
}

public sealed record IncidentStateView(
    string IncidentId,
    string WorkflowExecutionId,
    string? ActivityExecutionId,
    string? ExecutableNodeId,
    string Severity,
    string Status,
    string ResolutionAction,
    string FailureType,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool IsBlocking,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static IncidentStateView From(IncidentState state) =>
        new(
            state.IncidentId,
            state.WorkflowExecutionId,
            state.ActivityExecutionId,
            state.ExecutableNodeId,
            state.Severity.ToString(),
            state.Status.ToString(),
            state.ResolutionAction.ToString(),
            state.FailureType,
            state.Message,
            state.CreatedAt,
            state.ResolvedAt,
            state.IsBlocking,
            state.Metadata);
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
    string? SlotId = null)
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
            result.PinnedSource?.SlotId);
}
