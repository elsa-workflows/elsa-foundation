using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PublishActivityDraftEndpoint(
    IRequestSender requestSender,
    ILogger<PublishActivityDraftEndpoint> logger)
    : ElsaEndpoint<PublishActivityDraft, PublishedActivityDefinitionView>
{
    public override void Configure()
    {
        Post("design/activities/drafts/{draftId}/publish");
        ConfigurePermissions();
    }

    public override async Task HandleAsync(PublishActivityDraft request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(request, cancellationToken);
            HttpContext.Response.Headers.Location = $"/design/activities/versions/{response.VersionId}";
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
