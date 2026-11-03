using Elsa.Workflows.Runtime.Core.Contracts;
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
    public async Task Changed_durable_head_keeps_its_visibility_while_the_flush_delays_the_released_item()
    {
        // #3690: the flush persists the delay on the released item by identity, not on whatever is now the head.
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        Assert.True((await session.ReleaseOverlayClaimAsync(claim, _now.AddDays(2), default)).Succeeded);
        Assert.True(await _queue.DeleteAsync(ExecutionId, "resume"));
        await _queue.EnqueueAsync(Work("replacement-head"));
        await _queue.EnqueueAsync(Work("resume"));

        await session.AdvanceInnerQueueAsync(true, default);

        Assert.Equal([("replacement-head", (DateTimeOffset?)null), ("resume", _now.AddDays(2))],
            (await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items
            .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter)));
        var head = Assert.IsType<RuntimeSchedulerWorkClaim>(await _queue.ClaimAsync(Request("audit")));
        Assert.Equal("replacement-head", head.Item.WorkItemId);
        Assert.True((await _queue.CompleteClaimAsync(head)).Succeeded);
        Assert.Null(await _queue.ClaimAsync(Request("audit")));
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

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.ResumeBookmark)]
    [InlineData(WorkflowExecutionCommandKind.InvokeActivity)]
    [InlineData(WorkflowExecutionCommandKind.CompleteActivity)]
    [InlineData(WorkflowExecutionCommandKind.NotifyParentActivity)]
    [InlineData(WorkflowExecutionCommandKind.DeliverChildControl)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint)]
    public async Task Retained_source_can_be_deferred_after_a_boundary_deactivates_the_overlay(WorkflowExecutionCommandKind kind)
    {
        await _queue.EnqueueAsync(Work("resume", kind));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        var visibleAt = _now.AddDays(2);

        Assert.True((await session.ReleaseOverlayClaimAsync(claim, visibleAt, default)).Succeeded);

        var retained = Assert.Single((await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items);
        Assert.Equal(visibleAt, retained.VisibleAfter);
    }

    [Fact]
    public async Task Inactive_release_whose_durable_head_moved_delays_only_its_own_item()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        Assert.True(await _queue.DeleteAsync(ExecutionId, "resume"));
        await _queue.EnqueueAsync(Work("replacement-head"));
        await _queue.EnqueueAsync(Work("resume"));

        Assert.True((await session.ReleaseOverlayClaimAsync(claim, _now.AddDays(2), default)).Succeeded);

        Assert.False(session.OwnsOverlayClaim(claim));
        Assert.Equal([("replacement-head", (DateTimeOffset?)null), ("resume", _now.AddDays(2))],
            (await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items
            .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter)));
    }

    [Fact]
    public async Task Inactive_release_whose_durable_copy_another_owner_claimed_is_refused_while_the_claim_still_owns_the_item()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        var competing = Assert.IsType<RuntimeSchedulerWorkClaim>(await _queue.ClaimAsync(Request("other-owner")));

        var release = await session.ReleaseOverlayClaimAsync(claim, _now.AddDays(2), default);

        // Refused, not thrown after dropping the claim: the caller can still complete it, and the competing claim keeps
        // its own visibility rather than receiving the delay.
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, release.Status);
        Assert.True(session.OwnsOverlayClaim(claim));
        Assert.Equal(competing.VisibleAfter,
            Assert.Single((await _queue.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items).VisibleAfter);
        Assert.True((await _queue.ReleaseClaimAsync(competing, _now)).Succeeded);
    }

    [Fact]
    public async Task Completed_retained_notice_after_deactivation_removes_only_its_accepted_durable_identity()
    {
        await _queue.EnqueueAsync(Work("notice", WorkflowExecutionCommandKind.NotifyParentActivity));
        await _queue.EnqueueAsync(Work("sibling"));
        var session = Session();
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        Assert.True((await session.CompleteOverlayClaimAsync(claim, default)).Succeeded);
        Assert.Equal("sibling", Assert.Single((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items).WorkItemId);
    }

    [Fact]
    public async Task Stale_overlay_completion_after_deactivation_cannot_remove_unaccepted_durable_source()
    {
        await _queue.EnqueueAsync(Work("notice", WorkflowExecutionCommandKind.NotifyParentActivity));
        var session = Session();
        var old = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        Assert.NotNull(await session.ClaimOverlayAsync(new RuntimeSchedulerWorkClaimRequest(ExecutionId, "successor",
            _now.AddDays(2), TimeSpan.FromDays(1)), default));
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale,
            (await session.CompleteOverlayClaimAsync(old, default)).Status);
        Assert.Equal("notice", Assert.Single((await _queue.ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId))).Items).WorkItemId);
    }

    [Fact]
    public async Task Deferred_flush_delays_a_renewed_release_without_taking_a_durable_claim()
    {
        await _queue.EnqueueAsync(Work("resume"));
        var provider = new ClaimRecordingVisibilityQueue(_queue);
        var session = new RuntimeCoalescingSession(ExecutionId, provider, new PersistenceModeOptions());
        var initial = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(Request(), default));
        var renewal = await session.RenewOverlayClaimAsync(initial, _now.AddHours(2), TimeSpan.FromMinutes(3), default);
        var renewed = Assert.IsType<RuntimeSchedulerWorkClaim>(renewal.Claim);
        Assert.True((await session.ReleaseOverlayClaimAsync(renewed, _now.AddDays(2), default)).Succeeded);

        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);

        // The delay lands by identity, so no transient durable claim exists for a lease to outlive.
        Assert.Empty(provider.Requests);
        Assert.Equal(_now.AddDays(2), Assert.Single((await _queue.ListWithVisibilityAsync(
            new RuntimeSchedulerWorkQuery(ExecutionId))).Items).VisibleAfter);
    }

    private sealed class ClaimRecordingVisibilityQueue(InMemoryWorkflowSchedulerWorkQueue inner)
        : QueueWithoutVisibility(inner), IWorkflowSchedulerWorkVisibilityReader, IWorkflowSchedulerWorkDeferral
    {
        public List<RuntimeSchedulerWorkClaimRequest> Requests { get; } = [];
        public override ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken token = default)
        {
            Requests.Add(request);
            return base.ClaimAsync(request, token);
        }
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkVisibility>> ListWithVisibilityAsync(RuntimeSchedulerWorkQuery query, CancellationToken token = default) =>
            inner.ListWithVisibilityAsync(query, token);
        public ValueTask<bool> DeferAsync(string id, string workId, DateTimeOffset now, DateTimeOffset visibleAt, CancellationToken token = default) =>
            inner.DeferAsync(id, workId, now, visibleAt, token);
    }

    private RuntimeCoalescingSession Session() => new(ExecutionId, _queue, new PersistenceModeOptions());

    private RuntimeSchedulerWorkClaimRequest Request(string owner = "overlay-owner") =>
        new(ExecutionId, owner, _now, TimeSpan.FromDays(1));

    private RuntimeSchedulerWorkItem Work(string id, WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.ResumeBookmark) => new(
        id, ExecutionId, $"command-{id}", kind,
        $"envelope-{id}", $"key-{id}", _now, _now);
}
