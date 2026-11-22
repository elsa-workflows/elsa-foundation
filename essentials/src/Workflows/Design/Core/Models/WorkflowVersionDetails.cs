namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// One exact, immutable persisted workflow definition version: its identity, its definition's identity and name, and its
/// authored state, as the version details read (<c>GET versions/{versionId}</c>) composes them. Designer layout and
/// presentation records are not part of it; the version's creation time is in the definition's version list.
/// </summary>
public sealed record WorkflowVersionDetails(
    string VersionId,
    string DefinitionId,
    string Version,
    string DefinitionName,
    WorkflowDefinitionState State);
