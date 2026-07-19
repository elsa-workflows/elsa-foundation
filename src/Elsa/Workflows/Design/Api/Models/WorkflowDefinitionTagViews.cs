namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionMarkerTagView(
    string TagDefinitionId,
    string CanonicalKey,
    string DisplayName,
    string? Description,
    string? Color,
    string Status);

public sealed record WorkflowDefinitionTagAssertionView(
    string TagDefinitionId,
    string Origin,
    string OriginKey);

public sealed record WorkflowDefinitionTagSetView(
    string WorkflowDefinitionId,
    string Revision,
    IReadOnlyCollection<WorkflowDefinitionTagAssertionView> Assertions,
    bool CanAssign);

public sealed record ReplaceWorkflowDefinitionTagsRequest(
    string WorkflowDefinitionId,
    IReadOnlyCollection<string> TagDefinitionIds);

public sealed record WorkflowDefinitionTagConflictView(
    string Code,
    string CurrentRevision);
