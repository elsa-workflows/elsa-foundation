using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IActivityDefinitionVersionFactory versionFactory,
    IAddCommand<ActivityDefinitionVersion> addCommand,
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

        // Guard the author-supplied version against an existing (DefinitionId, sortKey) before the
        // add. The reconciler guards this on its path (hash-mismatch throw); the API path carries no
        // hash comparison to make, so any existing sort key is a collision. On Groundwork there is no
        // unique (DefinitionId, Version) constraint, so without this the duplicate persists silently.
        var sortKey = SemVer.ToSortKey(command.Version);
        if (await versionStore.FindByDefinitionAndSortKeyAsync(definition.Id, sortKey, cancellationToken) is not null)
            throw new ActivityDefinitionVersionConflictException(definition.Id, command.Version);

        await addCommand.Add(ActivityDefinitionVersion.From(version), cancellationToken);

        var addedVersion = await versionStore.GetWithDefinitionAsync(version.Id, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
