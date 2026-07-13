using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class ListInstances(IRequestSender requestSender, ILogger<ListInstances> logger)
    : ElsaEndpoint<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>
{
    public override void Configure()
    {
        Get(RouteConstants.Instances);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(ListWorkflowInstances request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await requestSender.Send(request.ForLegacyArray(), cancellationToken);
            await Send.OkAsync(result.Items, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
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
            logger.LogError(exception, "Unexpected error occurred when listing workflow instances.");
            ThrowError("Unexpected error occurred", 500);
        }
    }
}

internal sealed class ListInstancesPage(IRequestSender requestSender, ILogger<ListInstancesPage> logger)
    : ElsaRequestHandlerEndpoint<ListWorkflowInstances, WorkflowInstanceListView>(requestSender, logger)
{
    public override void Configure()
    {
        Get(RouteConstants.InstancesPage);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }
}
