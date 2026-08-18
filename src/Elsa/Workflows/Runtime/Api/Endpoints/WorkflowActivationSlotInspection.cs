using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class ListWorkflowActivationSlotsEndpoint(IRequestSender requestSender, ILogger<ListWorkflowActivationSlotsEndpoint> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowActivationSlots, WorkflowActivationSlotListView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.ActivationSlots);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}

internal sealed class GetWorkflowActivationSlotEndpoint(IRequestSender requestSender, ILogger<GetWorkflowActivationSlotEndpoint> logger)
    : ElsaRequestHandlerEndpoint<GetWorkflowActivationSlot, WorkflowActivationSlotView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.ActivationSlot);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}
