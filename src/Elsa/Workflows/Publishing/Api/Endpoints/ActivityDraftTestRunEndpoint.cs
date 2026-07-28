using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
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
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override async Task HandleAsync(StartActivityDraftTestRun request, CancellationToken cancellationToken)
    {
        var routedRequest = request with { DraftId = Route<string>("draftId")! };
        await ActivityDraftTestRunEndpointResponse.WriteAsync(
            HttpContext,
            logger,
            () => requestSender.Send(routedRequest, cancellationToken),
            202,
            cancellationToken);
    }
}

internal sealed class GetActivityDraftTestRunEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityDraftTestRunEndpoint> logger)
    : ElsaEndpointWithoutRequest<ActivityDraftTestRunView>
{
    public override void Configure()
    {
        Get("publishing/activity-test-runs/{testRunId}");
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        ActivityDraftTestRunEndpointResponse.WriteAsync(
            HttpContext,
            logger,
            () => requestSender.Send(
                new GetActivityDraftTestRun(Route<string>("testRunId")!),
                cancellationToken),
            200,
            cancellationToken);
}

internal sealed class GetActivityDraftTestRunByIdempotencyKeyEndpoint(
    IRequestSender requestSender,
    ILogger<GetActivityDraftTestRunByIdempotencyKeyEndpoint> logger)
    : ElsaEndpointWithoutRequest<ActivityDraftTestRunView>
{
    public override void Configure()
    {
        Get("publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}");
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        ActivityDraftTestRunEndpointResponse.WriteAsync(
            HttpContext,
            logger,
            () => requestSender.Send(
                new GetActivityDraftTestRunByIdempotencyKey(
                    Route<string>("draftId")!,
                    Route<string>("idempotencyKey")!),
                cancellationToken),
            200,
            cancellationToken);
}

internal sealed class CancelActivityDraftTestRunEndpoint(
    IRequestSender requestSender,
    ILogger<CancelActivityDraftTestRunEndpoint> logger)
    : ElsaEndpointWithoutRequest<ActivityDraftTestRunView>
{
    public override void Configure()
    {
        Post("publishing/activity-test-runs/{testRunId}/cancel");
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        ActivityDraftTestRunEndpointResponse.WriteAsync(
            HttpContext,
            logger,
            () => requestSender.Send(
                new CancelActivityDraftTestRun(Route<string>("testRunId")!),
                cancellationToken),
            202,
            cancellationToken);
}

internal static class ActivityDraftTestRunEndpointResponse
{
    public static async Task WriteAsync(
        HttpContext context,
        ILogger logger,
        Func<Task<ActivityDraftTestRunView>> action,
        int successStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.Response.SendAsync(await action(), successStatus, cancellation: cancellationToken);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(
                context.Response,
                ActivityPublishingProblems.Rejected(exception, context, "Activity draft Test Run rejected"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected activity draft Test Run failure");
            await ActivityPublishingProblems.WriteAsync(
                context.Response,
                ActivityPublishingProblems.Unexpected(context),
                cancellationToken);
        }
    }
}

public sealed class StartActivityDraftTestRunHandler(IActivityDraftTestRunService testRuns)
    : IRequestHandler<StartActivityDraftTestRun, ActivityDraftTestRunView>
{
    public Task<ActivityDraftTestRunView> Handle(StartActivityDraftTestRun request, CancellationToken cancellationToken) =>
        testRuns.StartAsync(request, cancellationToken);
}

public sealed class GetActivityDraftTestRunHandler(IActivityDraftTestRunService testRuns)
    : IRequestHandler<GetActivityDraftTestRun, ActivityDraftTestRunView>
{
    public Task<ActivityDraftTestRunView> Handle(GetActivityDraftTestRun request, CancellationToken cancellationToken) =>
        testRuns.GetAsync(request.TestRunId, cancellationToken);
}

public sealed class GetActivityDraftTestRunByIdempotencyKeyHandler(IActivityDraftTestRunService testRuns)
    : IRequestHandler<GetActivityDraftTestRunByIdempotencyKey, ActivityDraftTestRunView>
{
    public Task<ActivityDraftTestRunView> Handle(
        GetActivityDraftTestRunByIdempotencyKey request,
        CancellationToken cancellationToken) =>
        testRuns.GetByIdempotencyKeyAsync(request.DraftId, request.IdempotencyKey, cancellationToken);
}

public sealed class CancelActivityDraftTestRunHandler(IActivityDraftTestRunService testRuns)
    : IRequestHandler<CancelActivityDraftTestRun, ActivityDraftTestRunView>
{
    public Task<ActivityDraftTestRunView> Handle(CancelActivityDraftTestRun request, CancellationToken cancellationToken) =>
        testRuns.CancelAsync(request.TestRunId, cancellationToken);
}
