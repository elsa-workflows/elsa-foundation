using Elsa.Workflows.Design.Core.Models;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record WorkflowTestRun(
    string TestRunId,
    string DefinitionId,
    string DefinitionVersionId,
    string? ArtifactId,
    string? WorkflowExecutionId,
    WorkflowTestRunStatus Status,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    string? Reason,
    IReadOnlyDictionary<string, string> Metadata,
    TestRunDriverAction? DriverAction = null,
    JsonElement? DriverDetails = null);

public sealed record WorkflowTestRunDraftSnapshot(
    string DefinitionId,
    string DefinitionVersionId,
    string ArtifactVersion,
    WorkflowDefinitionState State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt);

public enum WorkflowTestRunStatus
{
    Rejected,
    DispatchAccepted,
    DispatchDuplicate,
    DispatchRejected,
    DispatchDeferred,
    Prepared,
    Stopped,
    StopRequested
}
