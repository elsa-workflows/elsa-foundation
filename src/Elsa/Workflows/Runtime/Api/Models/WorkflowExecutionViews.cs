using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record WorkflowExecutionView(
    string WorkflowExecutionId,
    string ArtifactId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ActivityExecutionView> Activities,
    string? Error)
{
    public static WorkflowExecutionView From(WorkflowExecutionResult result) =>
        new(
            result.WorkflowExecutionId,
            result.ArtifactId,
            result.Status.ToString(),
            result.StartedAt,
            result.CompletedAt,
            result.Activities.Select(ActivityExecutionView.From).ToArray(),
            result.Error);
}

public sealed record ActivityExecutionView(
    string ActivityExecutionId,
    string ExecutableNodeId,
    string ActivityType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error)
{
    public static ActivityExecutionView From(ActivityExecutionResult result) =>
        new(
            result.ActivityExecutionId,
            result.ExecutableNodeId,
            result.ActivityType,
            result.Status.ToString(),
            result.StartedAt,
            result.CompletedAt,
            result.Error);
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
