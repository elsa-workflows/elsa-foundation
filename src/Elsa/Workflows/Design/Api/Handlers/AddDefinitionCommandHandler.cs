using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
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

        var result = await addCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            WorkflowDefinition.From(definition),
            draftEntity,
            layout,
            cancellationToken);

        var authoritativeDefinition = WorkflowDefinition.From(definition);
        authoritativeDefinition.Id = result.DefinitionId;
        var authoritativeDraft = WorkflowDefinitionDraft.From(draft);
        authoritativeDraft.Id = result.DraftId;
        authoritativeDraft.WorkflowDefinitionId = result.DefinitionId;
        return new WorkflowDefinitionDetailsView(
            authoritativeDefinition.ToView(),
            WorkflowDraftView.From(authoritativeDraft, layout),
            Versions: []);
    }
}
