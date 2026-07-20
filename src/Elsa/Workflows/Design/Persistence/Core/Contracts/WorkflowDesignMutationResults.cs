namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>The authoritative identities produced by creating a workflow definition and its first draft.</summary>
public sealed record WorkflowDefinitionCreated(string DefinitionId, string DraftId);

/// <summary>The authoritative identity and semantic version produced by adding a workflow version.</summary>
public sealed record WorkflowDefinitionVersionAdded(
    string DefinitionId,
    string VersionId,
    string Version);
