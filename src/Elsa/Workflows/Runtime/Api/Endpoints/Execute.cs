using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class Execute(IRequestSender requestSender, ILogger<Execute> logger)
    : ElsaRequestHandlerEndpoint<ExecuteWorkflow, WorkflowExecutionView>(requestSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("{artifactId}/execute"));
        ConfigurePermissions();
    }
}
