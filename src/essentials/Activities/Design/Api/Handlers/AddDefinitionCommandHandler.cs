using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddDefinitionCommandHandler(
    IActivityDefinitionFactory definitionFactory,
    IActivityDefinitionVersionFactory versionFactory,
    IActivityDefinitionVersionStore versionStore,
    IAddActivityDefinitionCommand addCommand)

    : ICommandHandler<AddDefinition, ActivityDefinitionVersionDetailsView>
{
    private const string initialVersion = "1.0.0";

    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddDefinition command, CancellationToken cancellationToken)
    {
        var definition = definitionFactory.Create(command.ActivityTypeKey, command.Category, command.DisplayName, command.Description);
        var version = versionFactory.Create(
            definition,
            initialVersion,
            command.ProviderKey,
            command.ProviderSchemaVersion,
            command.ConsumerKey,
            command.ConsumerSchemaVersion,
            command.DescriptorPayload,
            command.SourceKind,
            command.SourceId,
            command.Inputs ?? [],
            command.Outputs ?? [],
            command.DesignFacets ?? [],
            command.ExecutionType ?? ActivityExecutionType.Action);

        var added = await addCommand.Execute(
            new DesignOperationKey(command.OperationKey),
            ActivityDefinition.From(definition),
            ActivityDefinitionVersion.From(version),
            cancellationToken);

        var addedVersion = await versionStore.GetWithDefinitionAsync(added.VersionId, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
