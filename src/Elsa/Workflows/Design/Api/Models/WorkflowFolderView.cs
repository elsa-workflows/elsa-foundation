namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowFolderView(
    string Id,
    string? ParentId,
    string Name,
    string NormalizedName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt);

public sealed record WorkflowFolderListView(IReadOnlyCollection<WorkflowFolderView> Items, string? NextContinuationToken = null);

public sealed record WorkflowFolderDetailsView(
    WorkflowFolderView Folder,
    IReadOnlyCollection<WorkflowFolderView> Ancestors);
