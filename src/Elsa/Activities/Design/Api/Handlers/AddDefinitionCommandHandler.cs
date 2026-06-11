using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IActivityDefinitionFactory definitionFactory,
    IActivityDefinitionVersionFactory versionFactory,
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

        var definition = definitionFactory.Create(command.ActivityTypeKey, command.Category, command.DisplayName, command.Description);
        var version = versionFactory.Create(
            definition,
            initialVersion,
            command.DescriptorType,
            command.DescriptorPayload,
            command.SourceKind,
            command.SourceId,
            command.Inputs ?? [],
            command.Outputs ?? [],
            command.DesignFacets ?? [],
            command.ExecutionType ?? ActivityExecutionType.Action);

        await addCommand.Execute(ActivityDefinition.From(definition), ActivityDefinitionVersion.From(version), cancellationToken);

        var addedVersion = await versionQueries.GetVersionInlcudingDefinition(version.Id, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
