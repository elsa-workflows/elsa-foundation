using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

public sealed class GetActivityExecutionLayoutEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityExecutionLayoutEndpoint> logger)
    : ElsaEndpoint<GetActivityExecutionLayout, ActivityExecutionLayoutView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout"));
        ConfigurePermissions();
    }

    public override async Task HandleAsync(GetActivityExecutionLayout req, CancellationToken ct)
    {
        try
        {
            var response = await requestSender.Send(req, ct);
            if (response.Layout is null)
                await ActivityExecutionProblemDetails.NotFoundAsync(HttpContext, ct);
            else
                await Send.OkAsync(response.Layout, ct);
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
            logger.LogError(exception, "Unexpected error while reading activity execution layout.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(HttpContext, ct);
        }
    }
}
