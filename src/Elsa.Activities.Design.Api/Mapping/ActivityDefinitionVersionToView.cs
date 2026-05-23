using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionVersionToView(IObjectMapper objectMapper) : IObjectMapping<ActivityDefinitionVersion, ActivityDefinitionVersionView>
{
    public ActivityDefinitionVersionView Map(ActivityDefinitionVersion source)
    {
        if(source.Definition is null)
            throw new InvalidOperationException("Definition is null");

        var definition = objectMapper.Map<ActivityDefinitionView>(source.Definition);

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
