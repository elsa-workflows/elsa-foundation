using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

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
