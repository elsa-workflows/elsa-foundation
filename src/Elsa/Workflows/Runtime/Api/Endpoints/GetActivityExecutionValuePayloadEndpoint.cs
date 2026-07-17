using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

public sealed class GetActivityExecutionValuePayloadEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityExecutionValuePayloadEndpoint> logger)
    : ElsaEndpoint<GetActivityExecutionValuePayload, ActivityExecutionValuePayloadView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute(
            "instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetActivityExecutionValuePayload req, CancellationToken ct)
    {
        try
        {
            var response = await requestSender.Send(req, ct);
            switch (response.Result.Outcome)
            {
                case ActivityExecutionValuePayloadReadOutcome.Resolved:
                    await Send.OkAsync(response.Result.Value!, ct);
                    break;
                case ActivityExecutionValuePayloadReadOutcome.Denied:
                    await ActivityExecutionProblemDetails.ForbiddenAsync(HttpContext, ct);
                    break;
                case ActivityExecutionValuePayloadReadOutcome.Unavailable:
                    await ActivityExecutionProblemDetails.ValueUnavailableAsync(HttpContext, ct);
                    break;
                default:
                    await ActivityExecutionProblemDetails.NotFoundAsync(HttpContext, ct);
                    break;
            }
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
            logger.LogError(exception, "Unexpected error while resolving activity execution value evidence.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(HttpContext, ct);
        }
    }
}
