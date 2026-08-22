using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;

public sealed class Handler(
    ISubmitWorkflowDefinitionCommand submitCommand,
    IWorkflowDefinitionVersionStore versionStore)
    : ICommandHandler<SubmitDefinition, SubmittedWorkflowDefinitionView>
{
    public async Task<SubmittedWorkflowDefinitionView> Handle(SubmitDefinition command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.State);

        var submitted = await submitCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.Name,
            command.Description,
            command.State.ToState(),
            cancellationToken);

        var version = await versionStore.GetWithDefinitionAsync(submitted.VersionId, cancellationToken);
        var versionView = version.ToDetailsView();

        return new SubmittedWorkflowDefinitionView(
            versionView.Definition,
            submitted.DraftId,
            versionView);
    }
}
