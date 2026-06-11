using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IActivityDefinitionVersionFactory versionFactory,
    IAddCommand<ActivityDefinitionVersion> addCommand,
    IQueries<ActivityDefinitionVersion> queries,
    IQueries<ActivityDefinition> definitionQueries)

    : ICommandHandler<AddVersion, ActivityDefinitionVersionDetailsView>
{
    public async Task<ActivityDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var definition = await definitionQueries.Get(command.DefinitionId, cancellationToken);

        // A version added through the API is API-sourced; provenance is keyed on the definition's
        // stable type key (the AddVersion command carries no source fields).
        var version = versionFactory.Create(
            definition,
            command.Version,
            command.DescriptorType,
            command.DescriptorPayload,
            sourceKind: "Api",
            sourceId: definition.ActivityTypeKey,
            command.Inputs ?? [],
            command.Outputs ?? [],
            command.DesignFacets ?? [],
            command.ExecutionType ?? ActivityExecutionType.Action);

        await addCommand.Add(ActivityDefinitionVersion.From(version), cancellationToken);

        var addedVersion = await queries.GetVersionInlcudingDefinition(version.Id, cancellationToken);
        return addedVersion.ToDetailsView();
    }
}
