using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IAddWorkflowDefinitionVersionCommand addCommand,
    IWorkflowDefinitionVersionStore versionStore)

    : ICommandHandler<AddVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var operationKey = DesignOperationKey.CreateOrGenerate(command.OperationKey);
        var result = await addCommand.Execute(
            operationKey,
            command.DefinitionId,
            command.State.ToState(),
            cancellationToken);
        var addedVersion = await versionStore.GetWithDefinitionAsync(result.VersionId, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
