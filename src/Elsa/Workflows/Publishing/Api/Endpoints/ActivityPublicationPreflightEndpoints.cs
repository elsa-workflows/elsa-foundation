using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PreflightActivityDraftPublicationEndpoint(
    IRequestSender requestSender,
    ILogger<PreflightActivityDraftPublicationEndpoint> logger)
    : ElsaEndpoint<PreflightActivityDraftPublication, ActivityPublicationPreflightView>
{
    public override void Configure()
    {
        Post("design/activities/drafts/{draftId}/publication-preflight");
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(
        PreflightActivityDraftPublication request,
        CancellationToken cancellationToken)
    {
        try
        {
            var routedRequest = request with { DraftId = Route<string>("draftId")! };
            await Send.OkAsync(await requestSender.Send(routedRequest, cancellationToken), cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Rejected(
                    exception,
                    HttpContext,
                    "Activity publication preflight was rejected"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected activity publication preflight failure");
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Unexpected(HttpContext),
                cancellationToken);
        }
    }
}

internal sealed class GetActivityPublicationReceiptEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityPublicationReceiptEndpoint> logger)
    : ElsaEndpointWithoutRequest<ActivityPublicationReceiptView>
{
    public override void Configure()
    {
        Get("design/activities/publications/{idempotencyKey}");
        ConfigurePermissions(PermissionNames.WorkflowPublishingRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new GetActivityPublicationReceipt(Route<string>("idempotencyKey")!);
            await Send.OkAsync(await requestSender.Send(request, cancellationToken), cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Rejected(
                    exception,
                    HttpContext,
                    "Activity publication receipt lookup was rejected"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected activity publication receipt lookup failure");
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Unexpected(HttpContext),
                cancellationToken);
        }
    }
}
