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

public sealed class AddVersionCommandHandler(
    IActivityDefinitionVersionFactory versionFactory,
    IAddActivityDefinitionVersionCommand addCommand,
    IActivityDefinitionVersionStore versionStore,
    IActivityDefinitionStore definitionStore)

    : ICommandHandler<AddVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);

        // A version added through the API is API-sourced; provenance is keyed on the definition's
        // stable type key (the AddVersion command carries no source fields).
        var version = versionFactory.Create(
            definition,
            command.Version,
            command.ProviderKey,
            command.ProviderSchemaVersion,
            command.ConsumerKey,
            command.ConsumerSchemaVersion,
            command.DescriptorPayload,
            sourceKind: "Api",
            sourceId: definition.ActivityTypeKey,
            command.Inputs ?? [],
            command.Outputs ?? [],
            command.DesignFacets ?? [],
            command.ExecutionType ?? ActivityExecutionType.Action);

        var added = await addCommand.Execute(
            new DesignOperationKey(command.OperationKey),
            ActivityDefinitionVersion.From(version),
            cancellationToken);

        var addedVersion = await versionStore.GetWithDefinitionAsync(added.VersionId, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
