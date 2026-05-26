using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IIdentityGenerator identityGenerator,
    IObjectMapper mapper,
    IAddWorkflowDefinitionCommand addCommand,
    IQueries<WorkflowDefinition> queries)

    : ICommandHandler<AddDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var draft = await BuildDraft(cancellationToken);
        var definition = BuildDefinition(command);

        await addCommand.Execute(definition, draft, cancellationToken);

        var addedDefinition = queries.GetDefinitionInlcudingDraft(definition.Id, cancellationToken);
        return await mapper.Map<WorkflowDefinitionDetailsView>(addedDefinition, cancellationToken);
    }

    private async ValueTask<WorkflowDefinitionDraft> BuildDraft(CancellationToken cancellationToken)
    {
        var state = await mapper.Map<WorkflowDefinitionState>(new WorkflowDefinitionStateView(), cancellationToken);

        return new()
        {
            Id = identityGenerator.Generate(),
            State = state
        };
    }

    private WorkflowDefinition BuildDefinition(AddDefinition def)
    {
        return new()
        {
            Id = identityGenerator.Generate(),
            Description = def.Description,
            MetaData = def.MetaData,
            Name = def.Name
        };
    }
}
