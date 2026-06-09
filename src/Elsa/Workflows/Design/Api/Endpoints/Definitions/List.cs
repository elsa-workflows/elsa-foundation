using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class List(IRequestSender requestSender, ILogger<List> logger) : ElsaRequestHandlerEndpoint<ListDefinitions, IEnumerable<WorkflowDefinitionView>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Definitions);
        AllowAnonymous();
    }
}
