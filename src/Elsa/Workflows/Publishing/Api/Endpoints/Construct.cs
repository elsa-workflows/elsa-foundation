using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// GET publishing/activities/{activityId}/construct — crosses the construction seam for one row.
/// </summary>
internal sealed class Construct(IRequestSender requestSender, ILogger<Construct> logger)
    : ElsaRequestHandlerEndpoint<ConstructActivity, ConstructedActivityView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("activities/{activityId}/construct"));
        AllowAnonymous();
    }
}
