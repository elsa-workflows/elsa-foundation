using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions;

internal sealed class ListVersions(IRequestSender requestSender, ILogger<List> logger) : ElsaRequestHandlerEndpoint<ListDefinitionVersions, IEnumerable<ActivityDefinitionVersionInfo>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
        AllowAnonymous();
    }
}
