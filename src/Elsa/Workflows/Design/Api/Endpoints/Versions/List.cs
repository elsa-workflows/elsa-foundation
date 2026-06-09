using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

internal sealed class List(IRequestSender requestSender, ILogger<List> logger) : ElsaRequestHandlerEndpoint<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionInfo>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("definitions/{definitionId}/versions"));
        AllowAnonymous();
    }
}
