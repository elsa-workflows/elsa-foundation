using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record WorkflowTestRunView(
    string TestRunId,
    string DefinitionId,
    string DefinitionVersionId,
    string? ArtifactId,
    string? WorkflowExecutionId,
    string Status,
    string? CommandDispatchStatus,
    string? Reason,
    DateTimeOffset? ExpiresAt)
{
    public static WorkflowTestRunView From(
        WorkflowTestRun testRun,
        WorkflowExecutionCommandDispatchStatus? commandDispatchStatus = null) =>
        new(
            testRun.TestRunId,
            testRun.DefinitionId,
            testRun.DefinitionVersionId,
            testRun.ArtifactId,
            testRun.WorkflowExecutionId,
            testRun.Status.ToString(),
            commandDispatchStatus?.ToString(),
            testRun.Reason,
            testRun.ExpiresAt);
}
