using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IIdentityGenerator identityGenerator,
    IObjectMapper mapper,
    IAddWorkflowDefinitionCommand addCommand)

    : ICommandHandler<AddDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var definition = BuildDefinition(command);
        var draft = await BuildDraft(definition.Id, cancellationToken);

        await addCommand.Execute(definition, draft, cancellationToken);

        return new WorkflowDefinitionDetailsView(
            await mapper.Map<WorkflowDefinitionView>(definition, cancellationToken),
            await mapper.Map<WorkflowDefinitionStateView>(draft, cancellationToken),
            Versions: []);
    }

    private async ValueTask<WorkflowDefinitionDraft> BuildDraft(string workflowDefinitionId, CancellationToken cancellationToken)
    {
        var state = await mapper.Map<WorkflowDefinitionState>(new WorkflowDefinitionStateView(), cancellationToken);

        return new()
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionId = workflowDefinitionId,
            State = state
        };
    }

    private WorkflowDefinition BuildDefinition(AddDefinition def)
    {
        return new()
        {
            Id = identityGenerator.Generate(),
            Description = def.Description,
            Name = def.Name
        };
    }
}
