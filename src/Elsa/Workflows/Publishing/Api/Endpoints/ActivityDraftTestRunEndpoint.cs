using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class ActivityDraftTestRunEndpoint(
    IRequestSender requestSender,
    ILogger<ActivityDraftTestRunEndpoint> logger)
    : ElsaEndpoint<StartActivityDraftTestRun, ActivityDraftTestRunView>
{
    public override void Configure()
    {
        Post("publishing/activity-drafts/{draftId}/test-runs");
        ConfigurePermissions();
    }

    public override async Task HandleAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken)
    {
        try
        {
            await Send.ResponseAsync(await requestSender.Send(request, cancellationToken), 202, cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Rejected(exception, HttpContext, "Activity draft test run rejected"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected activity draft test-run failure");
            await ActivityPublishingProblems.WriteAsync(
                HttpContext.Response,
                ActivityPublishingProblems.Unexpected(HttpContext),
                cancellationToken);
        }
    }
}

public sealed class StartActivityDraftTestRunHandler(IActivityDraftTestRunPublisher publisher)
    : IRequestHandler<StartActivityDraftTestRun, ActivityDraftTestRunView>
{
    public Task<ActivityDraftTestRunView> Handle(StartActivityDraftTestRun request, CancellationToken cancellationToken) =>
        publisher.StartAsync(request, cancellationToken);
}
