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
        var folder = new WorkflowFolder
        {
            Id = identities.Generate(),
            ParentFolderId = string.IsNullOrWhiteSpace(command.ParentId) ? null : command.ParentId,
            Name = name,
            NormalizedName = normalizedName
        };
        return (await folders.CreateAsync(folder, cancellationToken)).ToView();
    }
}
