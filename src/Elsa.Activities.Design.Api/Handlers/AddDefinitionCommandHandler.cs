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
    private const int initialVersion = 1;

    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var exists = await definitionQueries.Any(
            d => d.SourceKind == command.SourceKind && d.SourceId == command.SourceId && d.ActivityTypeKey == command.ActivityTypeKey,
            cancellationToken);
        if (exists)
        {
            throw new ArgumentException($"Activity definition with identity (SourceKind='{command.SourceKind}', SourceId='{command.SourceId}', ActivityTypeKey='{command.ActivityTypeKey}') already exists");
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
            SourceKind = def.SourceKind,
            SourceId = def.SourceId,
            ReconciledAt = clock.UtcNow,
            ReconciledBy = Environment.MachineName,
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
            ImplementationKind = command.ImplementationDescriptor.Kind,
            ImplementationDescriptor = command.ImplementationDescriptor,
            Inputs = command.Inputs ?? [],
            Outputs = command.Outputs ?? [],
            Ports = command.Ports ?? []
        };
    }
}
