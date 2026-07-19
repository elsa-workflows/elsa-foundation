namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionView(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    DateTimeOffset? DeletedAt = null,
    string? DraftId = null,
    string? LatestVersionId = null,
    string? LatestVersion = null,
    int VersionCount = 0,
    IReadOnlyCollection<WorkflowDefinitionMarkerTagView>? MarkerTags = null,
    IReadOnlyCollection<WorkflowDefinitionTagChipView>? TagChips = null,
    WorkflowDefinitionControlledTagGroupKeyView? Group = null
);

public sealed record WorkflowDefinitionListView(
    IReadOnlyCollection<WorkflowDefinitionView> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<WorkflowDefinitionControlledTagFacetView>? ControlledTagFacets = null,
    IReadOnlyCollection<WorkflowDefinitionControlledTagGroupView>? ControlledTagGroups = null);
