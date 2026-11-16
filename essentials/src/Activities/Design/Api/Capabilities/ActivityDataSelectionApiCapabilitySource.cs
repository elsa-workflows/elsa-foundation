using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;

namespace Elsa.Activities.Design.Api.Capabilities;

internal sealed class ActivityDataSelectionApiCapabilitySource : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([
            new("elsa.api.activity-data-selection", 1,
                [new("activity-data-context", "design/activities/drafts/{draftId}/activity-data/context", true)], "ActivitiesDesignApi")]);
    }
}
