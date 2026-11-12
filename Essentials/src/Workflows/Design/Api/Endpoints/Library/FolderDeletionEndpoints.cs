using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using MinimalEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Library;

[Get("folders/{folderId}/deletion-preview")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderDeletionPreviewEndpoint(IWorkflowLibraryStore? library = null,
    IWorkflowFolderDeletionStore? store = null) : ApiEndpoint<GetFolderDeletionPreview, FolderDeletionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowFolderDeletionPreview";
    public override async Task<FolderDeletionView> HandleAsync(GetFolderDeletionPreview request, CancellationToken cancellationToken)
    {
        WorkflowLibraryAccess.RequireOrganization(library, HttpContext);
        var preview = await FolderDeletionAccess.PreviewAsync(HttpContext, store, request.FolderId, cancellationToken);
        return FolderDeletionView.From(preview);
    }
}

[Post("folders/{folderId}/delete")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class FolderDeletionEndpoint(IWorkflowLibraryStore? library = null,
    IWorkflowFolderDeletionStore? store = null) : ApiEndpoint<DeleteWorkflowFolder>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "WorkflowFolderDeletion";
    public override async Task HandleAsync(DeleteWorkflowFolder request, CancellationToken cancellationToken)
    {
        WorkflowLibraryAccess.RequireOrganization(library, HttpContext);
        var actor = await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken);
        var preview = await FolderDeletionAccess.PreviewAsync(HttpContext, store, request.FolderId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.PreviewRevision) || request.PreviewRevision != preview.Revision)
            throw new WorkflowLibraryConflictException("Folder contents changed. Reload the deletion preview and confirm again.");
        await store!.DeleteFolderAsync(preview.Folder.Id, request.PreviewRevision, request.DeleteWorkflows, actor!,
            request.OperationKey, cancellationToken);
    }
}
