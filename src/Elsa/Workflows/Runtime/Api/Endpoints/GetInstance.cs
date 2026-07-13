using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class GetInstance(IRequestSender requestSender, ILogger<GetInstance> logger)
    : ElsaEndpoint<GetWorkflowInstance, WorkflowInstanceDetailsView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances/{workflowExecutionId}"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetWorkflowInstance req, CancellationToken ct)
    {
        try
        {
            var result = await requestSender.Send(req, ct);
            if (result.Instance is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.OkAsync(result.Instance, ct);
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
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(GetWorkflowInstance));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
