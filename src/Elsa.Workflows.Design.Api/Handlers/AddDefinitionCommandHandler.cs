using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Extensions;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    ISystemClock systemClock,
    IIdentityGenerator identityGenerator,
    IObjectMapper mapper,
    IAddCommand<WorkflowDefinition> addDefinitionCommand,
    IAddCommand<WorkflowDefinitionVersion> addVersionCommand,
    IQueries<WorkflowDefinitionVersion> queries)

    : ICommandHandler<AddDefinition, WorkflowDefinitionVersionView>
{
    const int initialVersion = 1;

    public async Task<WorkflowDefinitionVersionView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var definition = CreateDefinition(command);
        await addDefinitionCommand.Add(definition, cancellationToken);

        var state = mapper.Map<WorkflowDefinitionState>(
            new WorkflowDefinitionStateView(MetaData: command.MetaData)
        );
        var version = CreateVersion(definition, state);
        await addVersionCommand.Add(version, cancellationToken);

        var addedVersion = queries.GetVersionIncludingDefinition(version.Id, cancellationToken);
        return mapper.Map<WorkflowDefinitionVersionView>(addedVersion);
    }

    

    WorkflowDefinition CreateDefinition(AddDefinition def)
    {
        var now = systemClock.UtcNow;

        return new()
        {
            Id = identityGenerator.Generate(),
            Description = def.Description,
            LastModifiedAt = now,
            CreatedAt = now
        };
    }

    WorkflowDefinitionVersion CreateVersion(WorkflowDefinition definition, WorkflowDefinitionState state)
    {
        return new(definition.Id, initialVersion, systemClock.UtcNow)
        {
            Id = identityGenerator.Generate(),
            Definition = definition,
            State = state
        };
    }
}
