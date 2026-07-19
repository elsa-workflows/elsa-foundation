using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionMarkerTagView(
    string TagDefinitionId,
    string CanonicalKey,
    string DisplayName,
    string? Description,
    string? Color,
    string Status);

public sealed record WorkflowDefinitionTagChipView(
    string TagDefinitionId,
    string CanonicalKey,
    string DisplayName,
    string? Description,
    string? Color,
    string Status,
    string? ControlledValueId = null,
    string? ControlledValueCanonicalKey = null,
    string? ControlledValueDisplayName = null,
    string? ControlledValueDescription = null,
    string? ControlledValueColor = null,
    string? ControlledValueStatus = null,
    bool Conflict = false,
    IReadOnlyCollection<string>? ConflictControlledValueIds = null);

public sealed record WorkflowDefinitionControlledTagFacetValueView(
    string ControlledValueId,
    string CanonicalKey,
    string DisplayName,
    string? Description,
    string? Color,
    string Status,
    int SortOrder,
    int Count);

public sealed record WorkflowDefinitionControlledTagFacetView(
    string TagDefinitionId,
    IReadOnlyCollection<WorkflowDefinitionControlledTagFacetValueView> Values);

public sealed record WorkflowDefinitionControlledTagGroupView(
    string Kind,
    string? ControlledValueId,
    string Label,
    string? Color,
    int Count);

public sealed record WorkflowDefinitionControlledTagGroupKeyView(
    string Kind,
    string? ControlledValueId = null);

public sealed record WorkflowDefinitionTagAssertionView(
    string TagDefinitionId,
    string Origin,
    string OriginKey,
    string? ControlledValueId = null);

public sealed record WorkflowDefinitionTagSetView(
    string WorkflowDefinitionId,
    string Revision,
    IReadOnlyCollection<WorkflowDefinitionTagAssertionView> Assertions,
    bool CanAssign);

public sealed record ReplaceWorkflowDefinitionTagsRequest(
    string WorkflowDefinitionId,
    IReadOnlyCollection<string> TagDefinitionIds,
    IReadOnlyCollection<WorkflowDefinitionControlledTagValue>? ControlledValues = null);

public sealed record WorkflowDefinitionTagConflictView(
    string Code,
    string CurrentRevision);
