using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IIdentityGenerator identityGenerator,
    IAddCommand<WorkflowDefinitionVersion> addCommand,
    IQueries<WorkflowDefinitionVersion> queries,
    IQueries<WorkflowDefinition> definitionQueries,
    IObjectMapper objectMapper)

    : ICommandHandler<AddVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var definitionTask = definitionQueries.Get(command.DefinitionId, cancellationToken);
        var lastVersionTask = queries.FindLastVersion(command.DefinitionId, cancellationToken);

        var definition = await definitionTask;
        var lastVersion = await lastVersionTask;

        var nextVersionNumber = (lastVersion?.Version ?? 0) + 1;
        var state = await objectMapper.Map<WorkflowDefinitionState>(command.State, cancellationToken);
        var version = CreateVersion(nextVersionNumber, definition, state);

        await addCommand.Add(version, cancellationToken);
        var addedVersion = await queries.GetVersionIncludingDefinition(version.Id, cancellationToken);

        return await objectMapper.Map<WorkflowDefinitionVersionDetailsView>(addedVersion, cancellationToken);
    }

    private WorkflowDefinitionVersion CreateVersion(int version, WorkflowDefinition definition, WorkflowDefinitionState workflowDefinitionState)
    {
        return new(definition.Id, version, stateSource: null)
        {
            Id = identityGenerator.Generate(),
            Definition = definition
        };
    }
}
