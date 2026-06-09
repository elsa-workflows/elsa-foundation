using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>GET publishing/activities — the catalog rows this surface can construct.</summary>
internal sealed class List(IRequestSender requestSender, ILogger<List> logger)
    : ElsaRequestHandlerEndpoint<ListConstructableActivities, IEnumerable<ConstructableActivityView>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Activities);
        AllowAnonymous();
    }
}
