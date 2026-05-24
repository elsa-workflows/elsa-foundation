using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionVersionToView() : IObjectMapping<ActivityDefinitionVersion, ActivityDefinitionVersionView>
{
    public ActivityDefinitionVersionView Map(ActivityDefinitionVersion source)
    {
        return new(
            source.Id,
            source.Version,
            source.Kind,
            source.CreatedAt
        );
    }
}