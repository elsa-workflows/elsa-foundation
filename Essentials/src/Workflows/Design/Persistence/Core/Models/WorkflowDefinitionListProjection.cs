namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>
/// Provider-neutral persistence projection containing the authored lifecycle facts required to
/// enrich a workflow-definition list without loading each definition's draft and versions separately.
/// </summary>
public sealed record WorkflowDefinitionListProjection(
    string WorkflowDefinitionId,
    string? DraftId,
    string? LatestVersionId,
    string? LatestVersion,
    int VersionCount,
    string? DraftSourceVersionId = null,
    string? DraftSourceVersion = null,
    IReadOnlyList<WorkflowDefinitionVersionProjection>? Versions = null);

/// <summary>The stable identity and semantic version of one saved workflow-definition version.</summary>
public sealed record WorkflowDefinitionVersionProjection(string Id, string Version);
