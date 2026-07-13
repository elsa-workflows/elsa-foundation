using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

public sealed class ListIncidentsEndpoint(IRequestSender requestSender, ILogger<ListIncidentsEndpoint> logger)
    : ElsaEndpoint<ListIncidents, ListIncidentsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances/{workflowExecutionId}/incidents"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(ListIncidents req, CancellationToken ct)
    {
        try
        {
            var result = await requestSender.Send(req, ct);
            if (!result.WorkflowExists)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.OkAsync(result, ct);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(ListIncidents));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
