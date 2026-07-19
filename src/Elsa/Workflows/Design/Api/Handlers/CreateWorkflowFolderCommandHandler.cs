using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class CreateWorkflowFolderCommandHandler(
    IIdentityGenerator identities,
    IWorkflowFolderStore folders) : ICommandHandler<CreateWorkflowFolder, WorkflowFolderView>
{
    public async Task<WorkflowFolderView> Handle(CreateWorkflowFolder command, CancellationToken cancellationToken)
    {
        var (name, normalizedName) = WorkflowFolderNames.Normalize(command.Name);
        var id = identities.Generate();
        WorkflowFolderNames.ValidateIdentifier(id, nameof(id));
        if (command.ParentId is not null)
            WorkflowFolderNames.ValidateIdentifier(command.ParentId, nameof(command.ParentId));
        var folder = new WorkflowFolder
        {
            Id = id,
            ParentFolderId = command.ParentId,
            Name = name,
            NormalizedName = normalizedName
        };
        return (await folders.CreateAsync(folder, cancellationToken)).ToView();
    }
}
