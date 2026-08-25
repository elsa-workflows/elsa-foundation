using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Availability.GetSettings;

[Get("/design/activities/availability/settings")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IActivityAvailabilityOperations service) : ApiEndpoint<GetActivityAvailabilitySettings, ActivityAvailabilitySettings>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AvailabilityGetSettings";
        options.Accepts = ["*/*", "application/json"];
    }

    public override Task<ActivityAvailabilitySettings> HandleAsync(GetActivityAvailabilitySettings request, CancellationToken cancellationToken) =>
        service.GetSettingsAsync(request, cancellationToken);
}
