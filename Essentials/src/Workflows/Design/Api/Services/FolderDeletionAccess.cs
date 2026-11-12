using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Primitives.Exceptions;

namespace Elsa.Workflows.Design.Api.Services;

internal static class FolderDeletionAccess
{
    public static async Task<FolderDeletionPreview> PreviewAsync(HttpContext context, IWorkflowFolderDeletionStore? store,
        string folderId, CancellationToken token)
    {
        if (store is null) throw new WorkflowLibraryUnavailableException("This backend does not support deleting folders with contents.");
        await WorkflowLibraryAccess.SubjectAsync(context, true, token);
        await RequireAsync(new(FolderId: folderId));
        var library = context.RequestServices.GetRequiredService<IWorkflowLibraryStore>();
        var preview = await store.PreviewFolderDeletionAsync(folderId, token);
        // A provider can resolve aliases (for example GUID casing on SQL Server).
        // Resource policies must decide on the stored identity before exposing inventory.
        await RequireAsync(new(FolderId: preview.Folder.Id));
        var detail = await library.GetFolderAsync(preview.Folder.Id, token)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), preview.Folder.Id);
        await WorkflowLibraryAccess.RequireFolderReadAsync(context,
            detail.Ancestors.Select(folder => folder.Id).Append(detail.Folder.Id), token);
        await RequireAsync(new(FolderId: preview.Parent?.Id));
        foreach (var folder in preview.Descendants)
            await RequireAsync(new(FolderId: folder.Id));
        var authorizers = context.RequestServices.GetServices<IWorkflowDefinitionAuthorizer>().ToArray();
        foreach (var workflow in preview.Workflows)
        {
            await RequireAsync(new(DefinitionId: workflow.DefinitionId));
            if (authorizers is not [var authorizer] || !await authorizer.CanManageAsync(workflow.DefinitionId, token))
                throw new WorkflowLibraryAuthorizationException("A workflow in this folder cannot be changed.");
        }
        return preview;

        async Task RequireAsync(WorkflowLibraryResource resource)
        {
            await WorkflowLibraryAccess.RequireAsync(context, WorkflowDesignPermissions.Read, resource, token);
            await WorkflowLibraryAccess.RequireAsync(context, WorkflowDesignPermissions.Manage, resource, token);
        }
    }
}
