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
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Library;

[Get("folders")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FolderListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowFolders, WorkflowFolderPage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderList";
    public override async Task<WorkflowFolderPage> HandleAsync(ListWorkflowFolders request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
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
        var library = WorkflowLibraryAccess.Require(store);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        return await library.GetFolderAsync(request.FolderId, cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), request.FolderId);
    }
}

[Post("folders")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<CreateWorkflowFolder, WorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFolderCreate";
    public override async Task<WorkflowFolder> HandleAsync(CreateWorkflowFolder request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
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
        var library = WorkflowLibraryAccess.Require(store);
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
        var library = WorkflowLibraryAccess.Require(store);
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
        var library = WorkflowLibraryAccess.Require(store);
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
    public override async Task HandleAsync(MoveWorkflowDefinitions request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        WorkflowLibraryAccess.ValidateTargets(request.DefinitionIds);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(FolderId: request.FolderId), cancellationToken);
        foreach (var definitionId in request.DefinitionIds)
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage, new WorkflowLibraryResource(DefinitionId: definitionId), cancellationToken);
        await library.MoveDefinitionsAsync(request.DefinitionIds, request.FolderId, actor!, request.OperationKey ?? Guid.NewGuid().ToString("N"), cancellationToken);
    }
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
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, request.FavoritesOnly, cancellationToken);
        IReadOnlyList<WorkflowTagFilter>? filters = null;
        if (!string.IsNullOrWhiteSpace(request.TagFilters))
        {
            if (request.TagFilters.Length > 16_384) throw new ArgumentException("Tag filters exceed the supported size.");
            try { filters = JsonSerializer.Deserialize(request.TagFilters, WorkflowsDesignJsonOptions.WireContext.IReadOnlyListWorkflowTagFilter); }
            catch (JsonException) { throw new ArgumentException("Tag filters must be a valid JSON array."); }
            if (filters is null || filters.Any(filter => filter is null))
                throw new ArgumentException("Tag filters must be a JSON array of filter objects.");
        }
        var query = new WorkflowLibraryQuery(request.SearchTerm, request.State, request.FolderId, request.Unfiled,
            request.IncludeDescendants, filters, request.FavoritesOnly, request.Sort, request.PageSize, request.ContinuationToken, request.PublicationState);
        var readScope = await WorkflowLibraryAccess.ReadScopeAsync(HttpContext, cancellationToken);
        var page = await library.ListDefinitionsAsync(query, actor, readScope, cancellationToken);
        foreach (var item in page.Items)
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: item.Definition.Id), cancellationToken);
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
            var view = WorkflowLibraryProjection.ToView(item) with { PublicationState = null, PublishedVersionId = null, PublishedVersion = null };
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
        var library = WorkflowLibraryAccess.Require(store);
        var resource = new WorkflowLibraryResource(DefinitionId: request.DefinitionId);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, resource, cancellationToken);
        var organization = await library.GetOrganizationAsync(request.DefinitionId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowOrganization), request.DefinitionId);
        var permissions = await WorkflowLibraryAccess.PermissionsAsync(HttpContext, resource, cancellationToken);
        var definition = definitions is null ? null : await definitions.FindByIdAsync(request.DefinitionId, cancellationToken);
        if (definition is null || definition.DeletedAt is not null)
            permissions = permissions with { CanAssignTags = false, CanManageFolders = false };
        return WorkflowOrganizationView.From(organization, permissions);
    }
}

[Put("definitions/{definitionId}/organization")]
[RequirePermission(WorkflowDesignPermissions.AssignTags)]
public sealed class OrganizationReplaceEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ReplaceWorkflowTags, WorkflowOrganizationView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryOrganizationReplace";
    public override async Task<WorkflowOrganizationView> HandleAsync(ReplaceWorkflowTags request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var resource = new WorkflowLibraryResource(DefinitionId: request.DefinitionId);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, resource, cancellationToken);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.AssignTags, resource, cancellationToken);
        var organization = await library.ReplaceTagsAsync(request.DefinitionId, request.Revision, request.Tags, actor!,
            request.OperationKey ?? Guid.NewGuid().ToString("N"), cancellationToken);
        return WorkflowOrganizationView.From(organization, await WorkflowLibraryAccess.PermissionsAsync(HttpContext, resource, cancellationToken));
    }
}

[Get("tags")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class TagListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowTags, WorkflowTagPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagList";
    public override async Task<WorkflowTagPageView> HandleAsync(ListWorkflowTags request, CancellationToken cancellationToken)
    {
        var page = await WorkflowLibraryAccess.Require(store).ListTagsAsync(request.Search, request.PageSize, request.ContinuationToken, cancellationToken);
        return new(page.Items, page.NextContinuationToken, await WorkflowLibraryAccess.PermissionsAsync(HttpContext, null, cancellationToken));
    }
}

[Get("tags/{tagId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class TagGetEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<GetWorkflowTag, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagGet";
    public override async Task<WorkflowTag> HandleAsync(GetWorkflowTag request, CancellationToken cancellationToken) =>
        await WorkflowLibraryAccess.Require(store).GetTagAsync(request.TagId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowTag), request.TagId);
}

[Post("tags")]
[RequirePermission(WorkflowDesignPermissions.ManageTags)]
public sealed class TagCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<WorkflowTagInput, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagCreate";
    public override async Task<WorkflowTag> HandleAsync(WorkflowTagInput request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, null, cancellationToken);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        return await library.CreateTagAsync(request, actor!, cancellationToken);
    }
}

[Put("tags/{tagId}")]
[RequirePermission(WorkflowDesignPermissions.ManageTags)]
public sealed class TagUpdateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SaveWorkflowTag, WorkflowTag>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryTagUpdate";
    public override async Task<WorkflowTag> HandleAsync(SaveWorkflowTag request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
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
    public override async Task<IReadOnlyList<WorkflowTagEditOutcome>> HandleAsync(WorkflowTagBulkEdit request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        if (request.Targets is null || request.Targets.Any(target => target is null || string.IsNullOrWhiteSpace(target.Revision)))
            throw new ArgumentException("Every selected workflow requires its current organization revision.");
        WorkflowLibraryAccess.ValidateTargets(request.Targets?.Select(target => target.DefinitionId).ToArray());
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var allowed = new List<WorkflowTagEdit>();
        var outcomes = new Dictionary<string, WorkflowTagEditOutcome>(StringComparer.Ordinal);
        foreach (var target in request.Targets!)
        {
            var resource = new WorkflowLibraryResource(DefinitionId: target.DefinitionId);
            if (await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.Read, resource, cancellationToken) &&
                await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.AssignTags, resource, cancellationToken))
                allowed.Add(target);
            else outcomes.Add(target.DefinitionId, new(target.DefinitionId, "denied", Message: "This workflow cannot be changed."));
        }
        if (allowed.Count > 0)
            foreach (var outcome in await library.EditTagsAsync(request with { Targets = allowed }, actor!, cancellationToken))
                outcomes.Add(outcome.DefinitionId, outcome);
        return request.Targets!.Select(target => outcomes[target.DefinitionId]).ToArray();
    }
}

[Get("views")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewListEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<ListWorkflowViews, WorkflowLibraryViewsView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewList";
    public override async Task<WorkflowLibraryViewsView> HandleAsync(ListWorkflowViews request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var canShare = await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
        return new((await library.ListViewsAsync(actor!, cancellationToken)).Select(view => WorkflowLibraryViewView.From(view, actor!, canShare)).ToArray(), new(canShare));
    }
}

[Post("views")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewCreateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<WorkflowLibraryViewInput, WorkflowLibraryViewView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewCreate";
    public override async Task<WorkflowLibraryViewView> HandleAsync(WorkflowLibraryViewInput request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var canShare = await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
        if (request.Shared && !canShare) throw new WorkflowLibraryAuthorizationException("Sharing a workflow view requires permission.");
        return WorkflowLibraryViewView.From(await library.SaveViewAsync(null, request, actor!, canShare, cancellationToken), actor!, canShare);
    }
}

[Put("views/{viewId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewUpdateEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SaveWorkflowView, WorkflowLibraryViewView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewUpdate";
    public override async Task<WorkflowLibraryViewView> HandleAsync(SaveWorkflowView request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var canShare = await WorkflowLibraryAccess.AllowsAsync(HttpContext, WorkflowDesignPermissions.ShareViews, null, cancellationToken);
        if (request.Shared && !canShare) throw new WorkflowLibraryAuthorizationException("Sharing a workflow view requires permission.");
        return WorkflowLibraryViewView.From(await library.SaveViewAsync(request.ViewId, request.ToInput(), actor!, canShare, cancellationToken), actor!, canShare);
    }
}

[Delete("views/{viewId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class ViewDeleteEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<DeleteWorkflowView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryViewDelete";
    public override async Task HandleAsync(DeleteWorkflowView request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
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
    public override async Task HandleAsync(SetWorkflowFavorite request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: request.DefinitionId), cancellationToken);
        await library.SetFavoriteAsync(request.DefinitionId, true, (await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken))!, cancellationToken);
    }
}

[Delete("definitions/{definitionId}/favorite")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class FavoriteDeleteEndpoint(IWorkflowLibraryStore? store = null) : ApiEndpoint<SetWorkflowFavorite>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowLibraryFavoriteDelete";
    public override async Task HandleAsync(SetWorkflowFavorite request, CancellationToken cancellationToken)
    {
        var library = WorkflowLibraryAccess.Require(store);
        await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: request.DefinitionId), cancellationToken);
        await library.SetFavoriteAsync(request.DefinitionId, false, (await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken))!, cancellationToken);
    }
}
