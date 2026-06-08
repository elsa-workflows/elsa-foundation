using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IIdentityGenerator identityGenerator,
    ISystemClock clock,
    IObjectMapper mapper,
    IQueries<ActivityDefinitionVersion> versionQueries,
    IAddActivityDefinitionCommand addCommand,
    IQueries<ActivityDefinition> definitionQueries)

    : ICommandHandler<AddDefinition, ActivityDefinitionVersionDetailsView>
{
    private const string initialVersion = "1.0.0";

    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var exists = await definitionQueries.Any(
            d => d.ActivityTypeKey == command.ActivityTypeKey,
            cancellationToken);
        if (exists)
        {
            throw new ArgumentException($"Activity definition with ActivityTypeKey='{command.ActivityTypeKey}' already exists");
        }

        var definition = CreateDefinition(command);
        var version = CreateVersion(command, definition);
        await addCommand.Execute(definition, version, cancellationToken);

        var addedVersion = await versionQueries.GetVersionInlcudingDefinition(version.Id, cancellationToken);

        return await mapper.Map<ActivityDefinitionVersionDetailsView>(addedVersion, cancellationToken);
    }

    private ActivityDefinition CreateDefinition(AddDefinition def)
    {
        return new()
        {
            Id = identityGenerator.Generate(),
            ActivityTypeKey = def.ActivityTypeKey,
            Category = def.Category,
            Description = def.Description,
            DisplayName = def.DisplayName
        };
    }

    private ActivityDefinitionVersion CreateVersion(AddDefinition command, ActivityDefinition definition)
    {
        return new(initialVersion, definition.Id, executionType: command.ExecutionType ?? Core.Models.ActivityExecutionType.Action)
        {
            Id = identityGenerator.Generate(),
            DescriptorType = command.DescriptorType,
            DescriptorPayload = command.DescriptorPayload,
            Inputs = command.Inputs ?? [],
            Outputs = command.Outputs ?? [],
            Ports = command.Ports ?? [],
            SourceKind = command.SourceKind,
            SourceId = command.SourceId,
            ReconciledAt = clock.UtcNow,
            ReconciledBy = Environment.MachineName,
        };
    }
}
