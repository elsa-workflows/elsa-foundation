using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionDraftFactory draftFactory,
    IAddWorkflowDefinitionCommand addCommand)

    : ICommandHandler<AddDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var definition = definitionFactory.Create(command.Name, command.Description);
        var draft = draftFactory.Create(definition.Id, (command.InitialState ?? new WorkflowDefinitionStateView()).ToState());
        var layout = (command.Layout ?? [])
            .Select(record => new DesignMetadataRecord(
                record.NodeId,
                record.X,
                record.Y,
                record.Width,
                record.Height,
                record.AdditionalProperties))
            .ToArray();

        await addCommand.Execute(
            WorkflowDefinition.From(definition),
            WorkflowDefinitionDraft.From(draft),
            layout,
            cancellationToken);

        return new WorkflowDefinitionDetailsView(definition.ToView(), draft.State.ToStateView(), Versions: []);
    }
}
