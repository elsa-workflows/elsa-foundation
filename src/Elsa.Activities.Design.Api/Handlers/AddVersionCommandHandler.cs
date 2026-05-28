using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IIdentityGenerator identityGenerator,
    IAddCommand<ActivityDefinitionVersion> addCommand,
    IQueries<ActivityDefinitionVersion> queries,
    IQueries<ActivityDefinition> definitionQueries,
    IObjectMapper objectMapper)

    : ICommandHandler<AddVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var definition = await definitionQueries.Get(command.DefinitionId, cancellationToken);

        var lastVersion = await queries.FindLastVersion(command.DefinitionId, cancellationToken);
        var nextVersionNumber = (lastVersion?.Version ?? 0) + 1;
        var version = CreateVersion(command, nextVersionNumber, definition);

        await addCommand.Add(version, cancellationToken);
        var addedVersion = await queries.GetVersionInlcudingDefinition(version.Id, cancellationToken);

        return await objectMapper.Map<ActivityDefinitionVersionDetailsView>(addedVersion, cancellationToken);
    }

    private ActivityDefinitionVersion CreateVersion(AddVersion command, int version, ActivityDefinition definition)
    {
        return new(version, command.DefinitionId, kind: command.Kind ?? Core.Models.ActivityKind.Action)
        {
            Id = identityGenerator.Generate(),
            ActivityTypeKey = definition.ActivityTypeKey,
            ImplementationKind = command.ImplementationDescriptor.Kind,
            ImplementationDescriptor = command.ImplementationDescriptor,
            Inputs = command.Inputs ?? [],
            Outputs = command.Outputs ?? [],
            Ports = command.Ports ?? [],
            Definition = definition
        };
    }
}
