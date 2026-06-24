using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record WorkflowInstanceSummaryView(
    string WorkflowExecutionId,
    string ArtifactId,
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    string ArtifactHash,
    string Status,
    string? SubStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    string? ParentWorkflowExecutionId,
    string? TenantId,
    int ActivityCount,
    int IncidentCount)
{
    public static WorkflowInstanceSummaryView From(
        WorkflowExecutionState state,
        int activityCount = 0,
        int incidentCount = 0) =>
        new(
            state.WorkflowExecutionId,
            state.PinnedExecutable.ArtifactId,
            state.PinnedExecutable.DefinitionId,
            state.PinnedExecutable.DefinitionVersionId,
            state.PinnedExecutable.ArtifactVersion,
            state.PinnedExecutable.ArtifactHash,
            state.Status.ToString(),
            state.SubStatus,
            state.CreatedAt,
            state.StartedAt,
            state.UpdatedAt,
            state.CompletedAt,
            state.CorrelationId,
            state.ParentWorkflowExecutionId,
            state.TenantId,
            activityCount,
            incidentCount);
}

public sealed record WorkflowInstanceDetailsView(
    WorkflowInstanceSummaryView Instance,
    IReadOnlyCollection<ActivityExecutionStateView> Activities,
    IReadOnlyCollection<IncidentStateView> Incidents);

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
    string? Reason)
{
    public static WorkflowExecutionStartDispatchView From(WorkflowExecutionStartDispatchResult result) =>
        new(
            result.WorkflowExecutionId,
            result.PinnedExecutable.ArtifactId,
            result.PinnedExecutable.ArtifactVersion,
            result.PinnedExecutable.ArtifactHash,
            result.CommandDispatch.Status.ToString(),
            result.CommandDispatch.EnvelopeId,
            result.Agent.AgentId,
            result.Agent.ProviderName,
            result.CommandDispatch.Reason);
}
