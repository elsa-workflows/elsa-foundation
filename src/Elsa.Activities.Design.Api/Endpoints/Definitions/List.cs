using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions;

internal sealed class List(IRequestSender requestSender, ILogger<List> logger) : ElsaRequestHandlerEndpoint<ListDefinitions, IEnumerable<ActivityDefinitionView>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Definitions);
        AllowAnonymous();
    }
}
