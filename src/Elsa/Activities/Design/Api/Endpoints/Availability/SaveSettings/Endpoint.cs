using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Availability.SaveSettings;

[Put("/design/activities/availability/settings")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[LegacyProblems]
public sealed class Endpoint(ICommandSender sender) : ApiEndpoint<SaveActivityAvailabilitySettings, ActivityAvailabilitySettings>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AvailabilitySaveSettings";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
    }

    public override Task<ActivityAvailabilitySettings> HandleAsync(SaveActivityAvailabilitySettings command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
