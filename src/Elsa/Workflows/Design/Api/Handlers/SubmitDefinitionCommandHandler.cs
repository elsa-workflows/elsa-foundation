using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class SubmitDefinitionCommandHandler(
    ISubmitWorkflowDefinitionCommand submitCommand,
    IQueries<WorkflowDefinitionVersion> versionQueries)
    : ICommandHandler<SubmitDefinition, SubmittedWorkflowDefinitionView>
{
    public async Task<SubmittedWorkflowDefinitionView> Handle(SubmitDefinition command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.State);

        var submitted = await submitCommand.Execute(
            command.Name,
            command.Description,
            command.State.ToState(),
            cancellationToken);

        var version = await versionQueries.GetVersionIncludingDefinition(submitted.VersionId, cancellationToken);
        var versionView = version.ToDetailsView();

        return new SubmittedWorkflowDefinitionView(
            versionView.Definition,
            submitted.DraftId,
            versionView);
    }
}
