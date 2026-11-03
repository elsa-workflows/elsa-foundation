namespace Elsa.Agent.Workflows.Models;

public sealed record WorkflowAgentActivitySummary(
    string Id,
    string Type,
    string DisplayName);

public sealed record WorkflowAgentConnectionSummary(
    string Source,
    string Target,
    string? SourcePort,
    string? TargetPort);

/// <summary>
/// The live workflow graph the client (Studio) serialized into a <c>workflow.definition</c> attachment:
/// the existing activities, their connections, and the revision the edit is based on.
/// </summary>
public sealed record WorkflowAgentGraph(
    string? Revision,
    IReadOnlyCollection<WorkflowAgentActivitySummary> Activities,
    IReadOnlyCollection<WorkflowAgentConnectionSummary> Connections);
