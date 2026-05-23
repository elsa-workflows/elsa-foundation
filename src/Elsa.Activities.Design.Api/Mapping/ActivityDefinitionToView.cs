using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionToView : IObjectMapping<ActivityDefinition, ActivityDefinitionView>
{
    public ActivityDefinitionView Map(ActivityDefinition source)
    {
        return new(
            source.Id,
            source.UniqueName,
            source.Category,
            source.DisplayName,
            source.Description,
            source.IsBrowsable
        );
    }
}
