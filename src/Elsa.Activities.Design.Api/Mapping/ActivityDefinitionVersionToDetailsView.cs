using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionVersionToDetailsView(IObjectMapping<ActivityDefinition, ActivityDefinitionView> defMapping) : IObjectMapping<ActivityDefinitionVersion, ActivityDefinitionVersionDetailsView>
{
    public ActivityDefinitionVersionDetailsView Map(ActivityDefinitionVersion source)
    {
        if (source.Definition is null)
            throw new InvalidOperationException($"Mapping failed: source.Definition is null");

        var definition = defMapping.Map(source.Definition);
        return new(
            source.Id,
            source.Version,
            source.TypeInfo,
            definition,
            source.Inputs,
            source.Outputs,
            source.Ports,
            source.Kind
        );
    }
}
