using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowLibraryPermissions(bool CanAssignTags, bool CanManageFolders, bool CanManageTags, bool CanShareViews);
public sealed record WorkflowOrganizationView(string DefinitionId, string? FolderId,
    IReadOnlyList<WorkflowFolder> FolderBreadcrumb, IReadOnlyList<WorkflowTagAssignment> Tags, string Revision,
    WorkflowLibraryPermissions Permissions)
{
    public static WorkflowOrganizationView From(WorkflowOrganization organization, WorkflowLibraryPermissions permissions) =>
        new(organization.DefinitionId, organization.FolderId, organization.FolderBreadcrumb, organization.Tags, organization.Revision, permissions);
}
public sealed record WorkflowLibraryPageView(IReadOnlyList<WorkflowDefinitionView> Items,
    string? NextContinuationToken, int TotalCount, WorkflowLibraryPermissions Permissions);
public sealed record WorkflowTagPageView(IReadOnlyList<WorkflowTag> Items, string? NextContinuationToken,
    WorkflowLibraryPermissions Permissions);
public sealed record WorkflowViewPermissions(bool CanShareViews);
public sealed record WorkflowLibraryViewsView(IReadOnlyList<WorkflowLibraryViewView> Items, WorkflowViewPermissions Permissions);
public sealed record WorkflowLibraryViewView(string Id, string Name, bool Shared, string OwnerId,
    WorkflowLibraryQuery Query, IReadOnlyList<string> Columns, string Revision, int SchemaVersion, bool CanEdit, bool CanDelete)
{
    public static WorkflowLibraryViewView From(WorkflowLibraryView view, string subjectId, bool canShare)
    {
        var allowed = view.Shared ? canShare : string.Equals(view.OwnerId, subjectId, StringComparison.Ordinal);
        return new(view.Id, view.Name, view.Shared, view.OwnerId, view.Query, view.Columns, view.Revision, view.SchemaVersion, allowed, allowed);
    }
}

/// <summary>The resource supplied to host permission handlers for a definition or folder operation.</summary>
public sealed record WorkflowLibraryResource(string? DefinitionId = null, string? FolderId = null);

public sealed record ListWorkflowFolders(string? ParentId = null, int PageSize = 50, string? ContinuationToken = null);
public sealed record GetWorkflowFolder(string FolderId);
public sealed record CreateWorkflowFolder(string Name, string? ParentId = null);
public sealed record RenameWorkflowFolder(string FolderId, string Name, string? Revision = null);
public sealed record MoveWorkflowFolder(string FolderId, string? ParentId = null, string? Revision = null);
public sealed record DeleteEmptyWorkflowFolder(string FolderId, string? Revision = null);
public sealed record GetFolderDeletionPreview(string FolderId);
public sealed record DeleteWorkflowFolder(string FolderId, string PreviewRevision, bool DeleteWorkflows, string OperationKey);
public sealed record FolderDeletionView(WorkflowFolder Folder, WorkflowFolder? Parent, int WorkflowCount,
    int DeletedWorkflowCount, int ChildFolderCount, string Revision)
{
    public static FolderDeletionView From(FolderDeletionPreview preview) => new(preview.Folder, preview.Parent,
        preview.Workflows.Count(workflow => !workflow.Deleted), preview.Workflows.Count(workflow => workflow.Deleted),
        preview.Descendants.Count(folder => folder.ParentId == preview.Folder.Id), preview.Revision);
}
public sealed record MoveWorkflowDefinitions(IReadOnlyList<string> DefinitionIds, string? FolderId = null, string? OperationKey = null);
public sealed record ListWorkflowLibrary(string? SearchTerm = null, string State = "active", string? FolderId = null,
    bool Unfiled = false, bool IncludeDescendants = false, string? TagFilters = null, bool FavoritesOnly = false,
    string Sort = "name", int PageSize = 50, string? ContinuationToken = null, string? PublicationState = null);
public sealed record GetWorkflowOrganization(string DefinitionId);
public sealed record ReplaceWorkflowTags(string DefinitionId, IReadOnlyList<WorkflowTagAssignment> Tags,
    string Revision, string? OperationKey = null);
public sealed record ListWorkflowTags(string? Search = null, int PageSize = 50, string? ContinuationToken = null);
public sealed record GetWorkflowTag(string TagId);
public sealed record SaveWorkflowTag(string TagId, string Name, string? Description = null, string Mode = "marker",
    string Cardinality = "single", bool Deprecated = false, IReadOnlyList<WorkflowTagValue>? Values = null,
    string? Revision = null)
{
    public WorkflowTagInput ToInput() => new(Name, Description, Mode, Cardinality, Deprecated, Values, Revision);
}
/// <summary>The tag create body: the tag input plus an optional client operation key (spec 1193 FR-021), additive to the former body.</summary>
/// <remarks>The same key with the same input returns the tag that key created; with different input it is a 409. A store
/// without key support answers a keyed create with 501.</remarks>
public sealed record CreateWorkflowTag(string Name, string? Description = null, string Mode = "marker",
    string Cardinality = "single", bool Deprecated = false, IReadOnlyList<WorkflowTagValue>? Values = null,
    string? Revision = null, string? OperationKey = null)
{
    public WorkflowTagInput ToInput() => new(Name, Description, Mode, Cardinality, Deprecated, Values, Revision);
}
public sealed record ListWorkflowViews;
/// <summary>The view create body: the view input plus an optional client operation key (spec 1193 FR-021), additive to the former body.</summary>
public sealed record CreateWorkflowView(string Name, WorkflowLibraryQuery Query, bool Shared = false,
    IReadOnlyList<string>? Columns = null, string? Revision = null, string? OperationKey = null)
{
    public WorkflowLibraryViewInput ToInput() => new(Name, Query, Shared, Columns, Revision);
}
public sealed record SaveWorkflowView(string ViewId, string Name, WorkflowLibraryQuery Query, bool Shared = false,
    IReadOnlyList<string>? Columns = null, string? Revision = null)
{
    public WorkflowLibraryViewInput ToInput() => new(Name, Query, Shared, Columns, Revision);
}
public sealed record DeleteWorkflowView(string ViewId, string Revision);
public sealed record SetWorkflowFavorite(string DefinitionId);
