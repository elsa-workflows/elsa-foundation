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

        var version = CreateVersion(command, definition);

        await addCommand.Add(version, cancellationToken);
        var addedVersion = await queries.GetVersionInlcudingDefinition(version.Id, cancellationToken);

        return await objectMapper.Map<ActivityDefinitionVersionDetailsView>(addedVersion, cancellationToken);
    }

    private ActivityDefinitionVersion CreateVersion(AddVersion command, ActivityDefinition definition)
    {
        return new(command.Version, command.DefinitionId, executionType: command.ExecutionType ?? Core.Models.ActivityExecutionType.Action)
        {
            Id = identityGenerator.Generate(),
            DescriptorType = command.DescriptorType,
            DescriptorPayload = command.DescriptorPayload,
            Inputs = command.Inputs ?? [],
            Outputs = command.Outputs ?? [],
            Ports = command.Ports ?? [],
            Definition = definition
        };
    }
}
