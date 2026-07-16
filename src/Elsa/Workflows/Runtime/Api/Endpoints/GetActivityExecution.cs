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
                await ActivityExecutionProblemDetails.NotFoundAsync(HttpContext, ct);
                return;
            }

            await Send.OkAsync(result.ActivityExecution, ct);
        }
        catch (ArgumentException exception)
        {
            await ActivityExecutionProblemDetails.InvalidRequestAsync(HttpContext, exception.Message, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error occurred when handling request '{type}'", typeof(GetActivityExecution));
            await ActivityExecutionProblemDetails.UnexpectedAsync(HttpContext, ct);
        }
    }
}
