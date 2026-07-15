using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Endpoints.Availability;

internal sealed class ListDiagnostics(IRequestSender requestSender) : ElsaEndpointWithoutRequest<ActivityAvailabilityDiagnostics>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("availability/diagnostics"));
        ConfigurePermissions(PermissionNames.ActivityDesignRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = await requestSender.Send(new ListActivityAvailabilityDiagnostics(), cancellationToken);
        await Send.OkAsync(response, cancellationToken);
    }
}
