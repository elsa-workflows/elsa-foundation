using System.Text.Json.Serialization;

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
    string? FolderId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WorkflowFolderBreadcrumbItemView>? FolderBreadcrumb { get; init; }
}

public sealed record WorkflowFolderBreadcrumbItemView(string Id, string Name);

public sealed record WorkflowDefinitionListView(IReadOnlyCollection<WorkflowDefinitionView> Items);

public sealed record WorkflowDefinitionPageView(
    IReadOnlyCollection<WorkflowDefinitionView> Items,
    string? NextContinuationToken);
