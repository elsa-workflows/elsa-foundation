using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

public sealed class GetActivityExecutionDescendantsEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityExecutionDescendantsEndpoint> logger)
    : ElsaEndpoint<GetActivityExecutionDescendants, ActivityExecutionHierarchyPageView>
{
    public override void Configure()
    {
        Get(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetActivityExecutionDescendants req, CancellationToken ct)
    {
        try
        {
            var response = await requestSender.Send(req, ct);
            if (response.Page is null)
                await ActivityExecutionProblemDetails.NotFoundAsync(HttpContext, ct);
            else
                await Send.OkAsync(response.Page, ct);
        }
        catch (ActivityExecutionHierarchyCursorException exception)
        {
            await ActivityExecutionProblemDetails.CursorAsync(HttpContext, exception, ct);
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
            logger.LogError(exception, "Unexpected error while reading activity execution descendants.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(HttpContext, ct);
        }
    }
}
