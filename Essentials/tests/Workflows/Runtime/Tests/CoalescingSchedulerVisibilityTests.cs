using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingSchedulerVisibilityTests
{
    private const string ExecutionId = "visibility-run";
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly InMemoryWorkflowSchedulerWorkQueue _queue = new();

    [Fact]
    public async Task Active_durable_claim_hides_the_overlay_head_and_its_sibling()
    {
        await _queue.EnqueueAsync(Work("resume"));
        await _queue.EnqueueAsync(Work("sibling"));
        Assert.NotNull(await _queue.ClaimAsync(Request("other-owner")));
        var session = Session();

        Assert.Null(await session.ClaimOverlayAsync(Request(), default));
        Assert.Equal(2, (await session.ListOverlayAsync(new RuntimeSchedulerWorkQuery(ExecutionId), default)).Items.Count);
    }

    [Fact]
    public async Task Deferred_overlay_only_source_is_persisted_before_its_visibility_delay()
    {
        var session = Session();
        await session.EnqueueOverlayAsync(Work("resume"), default);
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        var visibleAt = _now.AddDays(2);
        Assert.True((await session.ReleaseOverlayClaimAsync(claim, visibleAt, default)).Succeeded);
        Assert.Empty((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);

        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);

        var retained = Assert.Single((await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);
        Assert.Equal("resume", retained.Item.WorkItemId);
        Assert.Equal(visibleAt, retained.VisibleAfter);
    }

    [Fact]
    public async Task Competing_durable_claim_refuses_the_flush_without_overwriting_its_visibility()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var overlayClaim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        var competing = Assert.IsType<RuntimeSchedulerWorkClaim>(await _queue.ClaimAsync(Request("other-owner")));
        Assert.True((await session.ReleaseOverlayClaimAsync(overlayClaim, _now.AddDays(2), default)).Succeeded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AdvanceInnerQueueAsync(true, default).AsTask());

        var remaining = Assert.Single((await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);
        Assert.Equal(competing.VisibleAfter, remaining.VisibleAfter);
        Assert.True((await _queue.ReleaseClaimAsync(competing, _now)).Succeeded);
    }

    [Fact]
    public async Task Changed_durable_head_is_released_without_receiving_another_items_delay()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        Assert.True((await session.ReleaseOverlayClaimAsync(claim, _now.AddDays(2), default)).Succeeded);
        Assert.True(await _queue.DeleteAsync(ExecutionId, "resume"));
        await _queue.EnqueueAsync(Work("replacement-head"));
        await _queue.EnqueueAsync(Work("resume"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AdvanceInnerQueueAsync(true, default).AsTask());

        var head = Assert.IsType<RuntimeSchedulerWorkClaim>(await _queue.ClaimAsync(
            new RuntimeSchedulerWorkClaimRequest(ExecutionId, "audit", DateTimeOffset.UtcNow, TimeSpan.FromDays(1))));
        Assert.Equal("replacement-head", head.Item.WorkItemId);
        Assert.Equal(2, (await _queue.ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumed_source_is_never_recreated_by_a_later_overlay_release(bool deactivate)
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);
        if (deactivate)
            session.Deactivate();

        var release = await session.ReleaseOverlayClaimAsync(claim, _now.AddDays(2), default);

        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, release.Status);
        Assert.Empty((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);
    }

    [Fact]
    public async Task Retained_source_can_be_deferred_after_a_boundary_deactivates_the_overlay()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        var visibleAt = _now.AddDays(2);

        Assert.True((await session.ReleaseOverlayClaimAsync(claim, visibleAt, default)).Succeeded);

        var retained = Assert.Single((await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);
        Assert.Equal(visibleAt, retained.VisibleAfter);
    }

    private RuntimeCoalescingSession Session() => new(ExecutionId, _queue, new PersistenceModeOptions());

    private RuntimeSchedulerWorkClaimRequest Request(string owner = "overlay-owner") =>
        new(ExecutionId, owner, _now, TimeSpan.FromDays(1));

    private RuntimeSchedulerWorkItem Work(string id) => new(
        id, ExecutionId, $"command-{id}", WorkflowExecutionCommandKind.ResumeBookmark,
        $"envelope-{id}", $"key-{id}", _now, _now);
}
