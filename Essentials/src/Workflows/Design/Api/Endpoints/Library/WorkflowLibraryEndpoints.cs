using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Library;

[Get("folders")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FolderListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowFolders, WorkflowFolderPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderList";
    public override async Task<WorkflowFolderPage> HandleAsync(ListWorkflowFolders request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(FolderId: request.ParentId), cancellationToken);
        var page = await library.ListFoldersAsync(request.ParentId, request.PageSize, request.ContinuationToken, cancellationToken);
        foreach (var folder in page.Items)
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(FolderId: folder.Id), cancellationToken);
        return page;
    }
}

[Get("folders/{folderId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FolderGetEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<GetWorkflowFolder, WorkflowFolderDetail>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderGet";
    public override async Task<WorkflowFolderDetail> HandleAsync(GetWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        var detail = await library.GetFolderAsync(request.FolderId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), request.FolderId);
        await WorkflowLibraryAccess.RequireFolderReadAsync(HttpContext, detail.Ancestors.Select(folder => folder.Id).Append(detail.Folder.Id), cancellationToken);
        return detail;
    }
}

[Post("folders")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<CreateWorkflowFolder, WorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderCreate";
    public override async Task<WorkflowFolder> HandleAsync(CreateWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.ParentId), cancellationToken);
        return await library.CreateFolderAsync(request.Name, request.ParentId, actor!, cancellationToken);
    }
}

[Post("folders/{folderId}/rename")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderRenameEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<RenameWorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderRename";
    public override async Task HandleAsync(RenameWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        await library.RenameFolderAsync(request.FolderId, request.Name, request.Revision, actor!, cancellationToken);
    }
}

[Post("folders/{folderId}/move")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderMoveEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<MoveWorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderMove";
    public override async Task HandleAsync(MoveWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.ParentId), cancellationToken);
        await library.MoveFolderAsync(request.FolderId, request.ParentId, request.Revision, actor!, cancellationToken);
    }
}

[Delete("folders/{folderId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderDeleteEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<DeleteEmptyWorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderDelete";
    public override async Task HandleAsync(DeleteEmptyWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        await library.DeleteEmptyFolderAsync(request.FolderId, request.Revision, actor!, cancellationToken);
    }
}

[Post("definitions/move")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class DefinitionFolderMoveEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<MoveWorkflowDefinitions>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryDefinitionFolderMove";
    public override Task HandleAsync(MoveWorkflowDefinitions request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).MoveDefinitionsAsync(HttpContext, request.DefinitionIds, request.FolderId, request.OperationKey, cancellationToken);
}

[Get("definitions/page")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class DefinitionPageEndpoint(IWorkflowLibraryStore? store = null,
    IWorkflowDefinitionPublicationStateLookup? publicationStateLookup = null) : ApiEndpoint<ListWorkflowLibrary, WorkflowLibraryPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryDefinitionPage";
    public override async Task<WorkflowLibraryPageView> HandleAsync(ListWorkflowLibrary request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        IReadOnlyList<WorkflowTagFilter>? filters = null;
        if (!string.IsNullOrWhiteSpace(request.TagFilters))
        {
            if (request.TagFilters.Length > 16_384) throw new ArgumentException("Tag filters exceed the supported size.");
            try { filters = JsonSerializer.Deserialize(request.TagFilters, WorkflowsDesignJsonOptions.WireContext.IReadOnlyListWorkflowTagFilter); }
            catch (JsonException) { throw new ArgumentException("Tag filters must be a valid JSON array."); }
            if (filters is null || filters.Any(filter => filter is null))
                throw new ArgumentException("Tag filters must be a JSON array of filter objects.");
        }
        WorkflowLibraryAccess.RequireUnconstrained(HttpContext, request.FolderId is not null || request.Unfiled ||
            request.IncludeDescendants || request.FavoritesOnly || filters is { Count: > 0 });
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, request.FavoritesOnly, cancellationToken);
        var query = new WorkflowLibraryQuery(request.SearchTerm, request.State, request.FolderId, request.Unfiled,
            request.IncludeDescendants, filters, request.FavoritesOnly, request.Sort, request.PageSize, request.ContinuationToken, request.PublicationState);
        var readScope = await WorkflowLibraryAccess.ReadScopeAsync(HttpContext, cancellationToken);
        var page = await library.ListDefinitionsAsync(query, actor, readScope, cancellationToken);
        foreach (var item in page.Items)
        {
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: item.Definition.Id), cancellationToken);
            await WorkflowLibraryAccess.RequireOrganizationReadAsync(HttpContext, item.Organization, cancellationToken);
        }
        var definitionIds = page.Items.Select(item => item.Definition.Id).ToArray();
        var publicationSummaries = publicationStateLookup is null || definitionIds.Length == 0
            ? null : await publicationStateLookup.ListByDefinitionIdsAsync(definitionIds, cancellationToken);
        var publishedVersionIds = publicationSummaries?.Where(pair => definitionIds.Contains(pair.Key, StringComparer.Ordinal))
            .Select(pair => pair.Value.PublishedVersionId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var labels = publishedVersionIds.Length == 0 ? null : await library.GetVersionLabelsAsync(publishedVersionIds, cancellationToken);
        var items = page.Items.Select(item =>
        {
            // The membership index answers the filter. Current publication identities and lifecycle
            // come from Publishing; unavailable evidence remains unknown rather than inferred.
            var view = WorkflowLibraryProjection.ToView(item, WorkflowLibraryAccess.OrganizationEnabled(HttpContext)) with { PublicationState = null, PublishedVersionId = null, PublishedVersion = null };
            return view.WithPublicationState(publicationSummaries, labels);
        }).ToArray();
        return new(items, page.NextContinuationToken, page.TotalCount,
            await WorkflowLibraryAccess.PermissionsAsync(HttpContext, null, cancellationToken));
    }
}

[Get("definitions/{definitionId}/organization")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class OrganizationGetEndpoint(IWorkflowLibraryStore? store = null,
    IWorkflowDefinitionStore? definitions = null) : ApiEndpoint<GetWorkflowOrganization, WorkflowOrganizationView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryOrganizationGet";
    public override async Task<WorkflowOrganizationView> HandleAsync(GetWorkflowOrganization request, CancellationToken cancellationToken)
    {
        var (organization, permissions) = await WorkflowLibraryCommands.Http(store).GetOrganizationAsync(HttpContext, request.DefinitionId, definitions, cancellationToken);
        return WorkflowOrganizationView.From(organization, permissions);
    }
}

[Put("definitions/{definitionId}/organization")]
[RequirePermission(WorkflowDesignPermissions.AssignTags)]
public sealed class OrganizationReplaceEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ReplaceWorkflowTags, WorkflowOrganizationView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryOrganizationReplace";
    public override Task<WorkflowOrganizationView> HandleAsync(ReplaceWorkflowTags request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).ReplaceTagsAsync(HttpContext, request, cancellationToken);
}

[Get("tags")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class TagListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowTags, WorkflowTagPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagList";
    public override Task<WorkflowTagPageView> HandleAsync(ListWorkflowTags request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).ListTagsAsync(HttpContext, request.Search, request.PageSize, request.ContinuationToken, cancellationToken);
}

[Get("tags/{tagId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class TagGetEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<GetWorkflowTag, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagGet";
    public override async Task<WorkflowTag> HandleAsync(GetWorkflowTag request, CancellationToken cancellationToken) =>
        await WorkflowLibraryAccess.RequireOrganization(store, HttpContext).GetTagAsync(request.TagId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowTag), request.TagId);
}

[Post("tags")]
[RequirePermission(WorkflowDesignPermissions.ManageTags)]
public sealed class TagCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<CreateWorkflowTag, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagCreate";
    public override Task<WorkflowTag> HandleAsync(CreateWorkflowTag request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).CreateTagAsync(HttpContext, request.ToInput(), request.OperationKey, cancellationToken);
}

[Put("tags/{tagId}")]
[RequirePermission(WorkflowDesignPermissions.ManageTags)]
public sealed class TagUpdateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SaveWorkflowTag, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagUpdate";
    public override async Task<WorkflowTag> HandleAsync(SaveWorkflowTag request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, null, cancellationToken);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        return await library.UpdateTagAsync(request.TagId, request.ToInput(), actor!, cancellationToken);
    }
}

[Post("tags/bulk")]
[RequirePermission(WorkflowDesignPermissions.AssignTags)]
public sealed class TagBulkEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<WorkflowTagBulkEdit, IReadOnlyList<WorkflowTagEditOutcome>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagBulk";
    public override Task<IReadOnlyList<WorkflowTagEditOutcome>> HandleAsync(WorkflowTagBulkEdit request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).EditTagsAsync(HttpContext, request, cancellationToken);
}

[Get("views")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowViews, WorkflowLibraryViewsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewList";
    public override Task<WorkflowLibraryViewsView> HandleAsync(ListWorkflowViews request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).ListViewsAsync(HttpContext, cancellationToken);
}

[Post("views")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<CreateWorkflowView, WorkflowLibraryViewView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewCreate";
    public override Task<WorkflowLibraryViewView> HandleAsync(CreateWorkflowView request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).SaveViewAsync(HttpContext, null, request.ToInput(), request.OperationKey, cancellationToken);
}

[Put("views/{viewId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewUpdateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SaveWorkflowView, WorkflowLibraryViewView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewUpdate";
    public override Task<WorkflowLibraryViewView> HandleAsync(SaveWorkflowView request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).SaveViewAsync(HttpContext, request.ViewId, request.ToInput(), null, cancellationToken);
}

[Delete("views/{viewId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewDeleteEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<DeleteWorkflowView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewDelete";
    public override async Task HandleAsync(DeleteWorkflowView request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.RequireOrganization(store, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await library.DeleteViewAsync(request.ViewId, request.Revision, actor!,
            await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.ShareViews, null, cancellationToken), cancellationToken);
    }
}

[Put("definitions/{definitionId}/favorite")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FavoritePutEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SetWorkflowFavorite>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFavoritePut";
    public override Task HandleAsync(SetWorkflowFavorite request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).SetFavoriteAsync(HttpContext, request.DefinitionId, true, cancellationToken);
}

[Delete("definitions/{definitionId}/favorite")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FavoriteDeleteEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SetWorkflowFavorite>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFavoriteDelete";
    public override Task HandleAsync(SetWorkflowFavorite request, CancellationToken cancellationToken) =>
        WorkflowLibraryCommands.Http(store).SetFavoriteAsync(HttpContext, request.DefinitionId, false, cancellationToken);
}
