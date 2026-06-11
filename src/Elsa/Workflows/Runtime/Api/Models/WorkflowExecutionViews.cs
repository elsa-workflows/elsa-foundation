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
