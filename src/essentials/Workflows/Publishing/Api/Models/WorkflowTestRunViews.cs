using Elsa.Workflows.Publishing.Core.Models;
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
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    public WorkflowTestRunView(
        string TestRunId,
        string DefinitionId,
        string DefinitionVersionId,
        string? ArtifactId,
        string? WorkflowExecutionId,
        string Status,
        string? CommandDispatchStatus,
        string? Reason,
        DateTimeOffset? ExpiresAt)
        : this(
            TestRunId,
            DefinitionId,
            DefinitionVersionId,
            ArtifactId,
            WorkflowExecutionId,
            Status,
            CommandDispatchStatus,
            Reason,
            ExpiresAt,
            new Dictionary<string, string>())
    {
    }

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
            testRun.ExpiresAt,
            testRun.Metadata);
}
