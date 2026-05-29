using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;

namespace Elsa.Activities.Design.Api.Mapping;

public sealed class ActivityDefinitionToView : IObjectMapping<ActivityDefinition, ActivityDefinitionView>
{
    public ValueTask<ActivityDefinitionView> Map(ActivityDefinition source, CancellationToken cancellationToken)
    {
        var result = new ActivityDefinitionView(
            source.Id,
            source.ActivityTypeKey,
            source.Category,
            source.DisplayName,
            source.Description
        );

        return new(result);
    }
}
