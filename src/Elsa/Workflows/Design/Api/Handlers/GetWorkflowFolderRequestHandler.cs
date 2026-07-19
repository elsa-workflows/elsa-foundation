using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetWorkflowFolderRequestHandler(IWorkflowFolderStore folders)
    : IRequestHandler<GetWorkflowFolder, WorkflowFolderDetailsView>
{
    public async Task<WorkflowFolderDetailsView> Handle(GetWorkflowFolder request, CancellationToken cancellationToken)
    {
        var details = await folders.FindWithAncestorsAsync(request.FolderId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), request.FolderId);
        return new WorkflowFolderDetailsView(details.Folder.ToView(), details.Ancestors.Select(folder => folder.ToView()).ToArray());
    }
}
