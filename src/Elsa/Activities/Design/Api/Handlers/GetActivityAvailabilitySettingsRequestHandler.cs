using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class GetActivityAvailabilitySettingsRequestHandler(IActivityAvailabilitySettingsStore settingsStore) : IRequestHandler<GetActivityAvailabilitySettings, ActivityAvailabilitySettings>
{
    public async Task<ActivityAvailabilitySettings> Handle(GetActivityAvailabilitySettings request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? ActivityAvailabilitySettings.HostDefaultScope
            : request.Scope;

        return await settingsStore.LoadAsync(scope, cancellationToken) ?? new ActivityAvailabilitySettings { Scope = scope };
    }
}
