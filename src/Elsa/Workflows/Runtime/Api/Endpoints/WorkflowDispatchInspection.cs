using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class ListWorkflowDispatchesEndpoint(
    IRequestSender requestSender,
    ILogger<ListWorkflowDispatchesEndpoint> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowDispatches, IReadOnlyCollection<WorkflowDispatchView>>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.Dispatches);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}

internal sealed class GetWorkflowDispatchEndpoint(
    IRequestSender requestSender,
    ILogger<GetWorkflowDispatchEndpoint> logger)
    : ElsaEndpoint<GetWorkflowDispatch, WorkflowDispatchView>
{
    public override void Configure()
    {
        Get(RouteConstants.Dispatch);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetWorkflowDispatch request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(request, cancellationToken);
            if (response.Dispatch is null)
            {
                await Send.NotFoundAsync(cancellationToken);
                return;
            }

            await Send.OkAsync(response.Dispatch, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error occurred when getting workflow dispatch {DispatchId}", request.DispatchId);
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
