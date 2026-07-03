using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class DispatchStimulusRequestHandlerTests
{
    [Fact]
    public async Task Handle_MapsRequestOntoRouter_AndProjectsResult()
    {
        var router = new RecordingRouter(new StimulusRoutingResult(
            [StimulusStartOutcome.Started("artifact-1:node-a", "artifact-1", "wfexec-new-1")],
            [new StimulusResumeOutcome("wfexec-1", BookmarkResumeDispatchStatus.Dispatched)]));
        var handler = new DispatchStimulusRequestHandler(router);

        var response = await handler.Handle(
            new DispatchStimulus("Event", "sha256:event:hello", CorrelationId: "order-7", IdempotencyKey: "delivery-1"),
            CancellationToken.None);

        Assert.Equal(1, response.StartedCount);
        Assert.Equal(1, response.ResumedCount);
        Assert.Equal("Event", router.LastRequest!.StimulusType);
        Assert.Equal("order-7", router.LastRequest.CorrelationId);
        Assert.Equal("delivery-1", router.LastRequest.IdempotencyKey);
        Assert.Equal(StimulusRoutingMode.StartAndResume, router.LastRequest.Mode);
    }

    [Fact]
    public async Task Handle_ParsesModeCaseInsensitively()
    {
        var router = new RecordingRouter(new StimulusRoutingResult([], []));
        var handler = new DispatchStimulusRequestHandler(router);

        await handler.Handle(new DispatchStimulus("Event", "sha256:event:hello", Mode: "startonly"), CancellationToken.None);

        Assert.Equal(StimulusRoutingMode.StartOnly, router.LastRequest!.Mode);
    }

    [Fact]
    public async Task Handle_ThrowsOnUnknownMode()
    {
        var handler = new DispatchStimulusRequestHandler(new RecordingRouter(new StimulusRoutingResult([], [])));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new DispatchStimulus("Event", "sha256:event:hello", Mode: "sideways"), CancellationToken.None));
    }

    private sealed class RecordingRouter(StimulusRoutingResult result) : IStimulusRouter
    {
        public StimulusDispatchRequest? LastRequest { get; private set; }

        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return new ValueTask<StimulusRoutingResult>(result);
        }
    }
}
