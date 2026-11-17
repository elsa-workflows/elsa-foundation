namespace Elsa.Weaver.Workflows.Models;

public sealed record WorkflowWeaverActivitySummary(
    string Id,
    string Type,
    string DisplayName);

public sealed record WorkflowWeaverConnectionSummary(
    string Source,
    string Target,
    string? SourcePort,
    string? TargetPort);

/// <summary>
/// The live workflow graph the client (Studio) serialized into a <c>workflow.definition</c> attachment:
/// the existing activities, their connections, and the revision the edit is based on.
/// </summary>
public sealed record WorkflowWeaverGraph(
    string? Revision,
    IReadOnlyCollection<WorkflowWeaverActivitySummary> Activities,
    IReadOnlyCollection<WorkflowWeaverConnectionSummary> Connections);
