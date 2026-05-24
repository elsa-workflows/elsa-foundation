using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions;

internal sealed class ListVersions : ElsaRequestHandlerEndpoint<ListDefinitionVersions, IEnumerable<ActivityDefinitionVersionView>>
{
    public ListVersions(IRequestSender requestSender, ILogger<List> logger) : base(requestSender, logger)
    {
        Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
        AllowAnonymous();
    }
}
