using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionVersionToDetailsView(IObjectMapping<ActivityDefinition, ActivityDefinitionView> defMapping)
    : IObjectMapping<ActivityDefinitionVersion, ActivityDefinitionVersionDetailsView>
{
    public async ValueTask<ActivityDefinitionVersionDetailsView> Map(ActivityDefinitionVersion source, CancellationToken cancellationToken)
    {
        if (source.Definition is null)
            throw new InvalidOperationException($"Mapping failed: source.Definition is null");

        var definition = await defMapping.Map(source.Definition, cancellationToken);
        return new(
            source.Id,
            source.Version,
            source.ActivityTypeKey,
            source.ImplementationKind,
            source.ImplementationDescriptor,
            definition,
            source.Inputs,
            source.Outputs,
            source.Ports,
            source.ExecutionType
        );
    }
}
