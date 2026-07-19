using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class RenameWorkflowFolderCommandHandler(IRestructureWorkflowFoldersCommand command)
    : ICommandHandler<RenameWorkflowFolder, WorkflowFolderView>
{
    public async Task<WorkflowFolderView> Handle(RenameWorkflowFolder request, CancellationToken cancellationToken)
    {
        WorkflowFolderNames.ValidateIdentifier(request.FolderId, nameof(request.FolderId));
        return (await command.RenameAsync(request.FolderId, request.Name, cancellationToken)).ToView();
    }
}

public sealed class MoveWorkflowFolderCommandHandler(IRestructureWorkflowFoldersCommand command)
    : ICommandHandler<MoveWorkflowFolder, WorkflowFolderView>
{
    public async Task<WorkflowFolderView> Handle(MoveWorkflowFolder request, CancellationToken cancellationToken)
    {
        WorkflowFolderNames.ValidateIdentifier(request.FolderId, nameof(request.FolderId));
        if (request.ParentId is not null)
            WorkflowFolderNames.ValidateIdentifier(request.ParentId, nameof(request.ParentId));
        return (await command.MoveAsync(request.FolderId, request.ParentId, cancellationToken)).ToView();
    }
}

public sealed class DeleteWorkflowFolderCommandHandler(IRestructureWorkflowFoldersCommand command)
    : ICommandHandler<DeleteWorkflowFolder>
{
    public async Task<Unit> Handle(DeleteWorkflowFolder request, CancellationToken cancellationToken)
    {
        WorkflowFolderNames.ValidateIdentifier(request.FolderId, nameof(request.FolderId));
        await command.DeleteEmptyAsync(request.FolderId, cancellationToken);
        return Unit.Instance;
    }
}
