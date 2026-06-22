using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Workflows.Models;

public sealed record WorkflowAgentContextRequest(
    string SessionId,
    string WorkflowDefinitionId,
    string? WorkflowVersionId);

public sealed record WorkflowAgentContext(
    string WorkflowDefinitionId,
    string? WorkflowVersionId,
    string Revision,
    string Summary,
    IReadOnlyCollection<WorkflowAgentActivitySummary> Activities,
    IReadOnlyCollection<WorkflowAgentDiagnosticSummary> Diagnostics,
    IReadOnlyCollection<string> Redactions);

public sealed record WorkflowAgentActivitySummary(
    string Id,
    string Type,
    string DisplayName);

public sealed record WorkflowAgentDiagnosticSummary(
    string Severity,
    string Message);

public sealed record WorkflowChangeProposalRequest(
    string SessionId,
    string ActorId,
    string WorkflowDefinitionId,
    string BaseRevision,
    string Title,
    string Summary,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Operations,
    IReadOnlyCollection<string> Risks,
    string? Rollback);
