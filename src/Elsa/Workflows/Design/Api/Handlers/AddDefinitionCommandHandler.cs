using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionDraftFactory draftFactory,
    IAddWorkflowDefinitionCommand addCommand)

    : ICommandHandler<AddDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        if (command.FolderId is { } folderId)
            WorkflowFolderNames.ValidateIdentifier(folderId, nameof(command.FolderId));
        var definition = definitionFactory.Create(command.Name, command.Description);
        var draft = draftFactory.Create(definition.Id, (command.InitialState ?? new WorkflowDefinitionStateView()).ToState());
        var draftEntity = WorkflowDefinitionDraft.From(draft);
        var layout = (command.Layout ?? [])
            .Select(record => new DesignMetadataRecord(
                record.NodeId,
                record.X,
                record.Y,
                record.Width,
                record.Height,
                record.AdditionalProperties))
            .ToArray();

        var persistenceDefinition = WorkflowDefinition.From(definition);
        persistenceDefinition.FolderId = command.FolderId;
        await addCommand.Execute(
            persistenceDefinition,
            draftEntity,
            layout,
            cancellationToken);

        return new WorkflowDefinitionDetailsView(persistenceDefinition.ToView(), WorkflowDraftView.From(draftEntity, layout), Versions: []);
    }
}
