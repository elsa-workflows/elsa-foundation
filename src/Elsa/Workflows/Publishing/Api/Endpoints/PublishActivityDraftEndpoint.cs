using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PublishActivityDraftEndpoint(
    IRequestSender requestSender,
    ILogger<PublishActivityDraftEndpoint> logger)
    : ElsaEndpoint<PublishActivityDraft, ActivityPublicationReceiptView>
{
    public override void Configure()
    {
        Post("design/activities/drafts/{draftId}/publish");
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override async Task HandleAsync(PublishActivityDraft request, CancellationToken cancellationToken)
    {
        try
        {
            var routedRequest = request with { DraftId = Route<string>("draftId")! };
            var response = await requestSender.Send(routedRequest, cancellationToken);
            HttpContext.Response.Headers.Location =
                $"/design/activities/publications/{Uri.EscapeDataString(response.IdempotencyKey)}";
            await Send.ResponseAsync(response, StatusCodes.Status201Created, cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Rejected(exception, HttpContext, "Activity publication was rejected"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected activity draft publication failure");
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Unexpected(HttpContext),
                cancellationToken);
        }
    }
}
