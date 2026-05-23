using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IIdentityGenerator identityGenerator,
    IObjectMapper mapper,
    IAddCommand<ActivityDefinition> addDefinitionCommand,
    IAddCommand<ActivityDefinitionVersion> addVersionCommand,
    IQueries<ActivityDefinition> definitionQueries)

    : ICommandHandler<AddDefinitionCommand, ActivityDefinitionVersionView>
{
    const int initialVersion = 1;

    public async Task<ActivityDefinitionVersionView> Handle(AddDefinitionCommand command, CancellationToken cancellationToken)
    {
        var exists = await definitionQueries.Any(d => d.UniqueName == command.UniqueName, cancellationToken);
        if (exists)
        {
            throw new ArgumentException($"Activity definition with UniqueName '{command.UniqueName}' already exists");
        }

        var definition = CreateDefinition(command);
        await addDefinitionCommand.Add(definition, cancellationToken);

        var version = CreateVersion(command, definition);
        await addVersionCommand.Add(version, cancellationToken);

        return mapper.Map<ActivityDefinitionVersionView>(version);
    }

    ActivityDefinition CreateDefinition(AddDefinitionCommand def)
    {
        return new()
        {
            UniqueName = def.UniqueName,
            Category = def.Category,
            Description = def.Description,
            DisplayName = def.DisplayName,
            Id = identityGenerator.Generate()
        };
    }

    ActivityDefinitionVersion CreateVersion(AddDefinitionCommand command, ActivityDefinition definition)
    {
        return new(command.TypeInfo, initialVersion, definition.Id, kind: command.Kind ?? Core.Models.ActivityKind.Action)
        {
            Id = identityGenerator.Generate(),
            Inputs = command.Inputs ?? [],
            Outputs = command.Outputs ?? [],
            Ports = command.Ports ?? [],
            Definition = definition
        };
    }
}
