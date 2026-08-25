using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Design.Api.Endpoints.Availability.ListDiagnostics;

[Get("/design/activities/availability/diagnostics")]
[RequirePermission(ActivityDesignPermissions.Read)]
[LegacyProblems]
public sealed class Endpoint(IActivityAvailabilityOperations service) : ApiEndpointWithoutRequest<ActivityAvailabilityDiagnostics>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "AvailabilityListDiagnostics";

    public override Task<ActivityAvailabilityDiagnostics> HandleAsync(CancellationToken cancellationToken) =>
        service.ListDiagnosticsAsync(new ListActivityAvailabilityDiagnostics(), cancellationToken);
}
