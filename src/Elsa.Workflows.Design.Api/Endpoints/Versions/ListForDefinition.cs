using Elsa.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

internal sealed class List : ElsaRequestHandlerEndpoint<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionView>>
{
    public List(IRequestSender requestSender, ILogger<List> logger) : base(requestSender, logger)
    {
        Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
        AllowAnonymous();
    }
}
