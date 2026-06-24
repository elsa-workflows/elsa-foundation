using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class ListInstances(IRequestSender requestSender, ILogger<ListInstances> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances"));
        ConfigurePermissions();
    }
}
