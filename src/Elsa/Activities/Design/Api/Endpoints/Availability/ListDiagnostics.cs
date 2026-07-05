using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Availability;

internal sealed class ListDiagnostics(IRequestSender requestSender, ILogger<ListDiagnostics> logger) : ElsaRequestHandlerEndpoint<ListActivityAvailabilityDiagnostics, ActivityAvailabilityDiagnostics>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("availability/diagnostics"));
        ConfigurePermissions();
    }
}
