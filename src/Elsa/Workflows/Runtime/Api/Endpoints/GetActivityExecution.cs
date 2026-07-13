using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

public sealed class GetActivityExecutionEndpoint(IRequestSender requestSender, ILogger<GetActivityExecutionEndpoint> logger)
    : ElsaEndpoint<GetActivityExecution, ActivityExecutionInspectionView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetActivityExecution req, CancellationToken ct)
    {
        try
        {
            var result = await requestSender.Send(req, ct);
            if (result.ActivityExecution is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.OkAsync(result.ActivityExecution, ct);
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
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(GetActivityExecution));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
