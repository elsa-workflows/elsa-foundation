using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListWorkflowFoldersRequestHandler(IWorkflowFolderStore folders)
    : IRequestHandler<ListWorkflowFolders, WorkflowFolderListView>
{
    public async Task<WorkflowFolderListView> Handle(ListWorkflowFolders request, CancellationToken cancellationToken)
    {
        var page = await folders.ListDirectChildrenAsync(
            new WorkflowFolderPageRequest(request.ParentId, Math.Clamp(request.PageSize ?? 100, 1, 100), request.ContinuationToken),
            cancellationToken);
        return new(page.Items.Select(folder => folder.ToView()).ToArray(), page.NextContinuationToken);
    }
}
