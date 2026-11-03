using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkItems;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfSchedulerWorkQueueStoreTests
{
    [Fact]
    public async Task Deferred_work_retains_its_visibility_and_identity_after_reopening_the_database_adapter()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var delay = TimeSpan.FromSeconds(5);
        var original = Work("wf-deferred", "work-1", 1, WorkflowExecutionCommandKind.ResumeBookmark);
        await using var database = await TestDatabase.CreateAsync();
        var handler = new UnavailableOnceHandler();
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        await using (var first = database.Open("tenant-a"))
        {
            await first.Store.EnqueueAsync(original);
            var result = await Drainer(first.Store).DrainAsync(new(original.WorkflowExecutionId, maxWorkItems: 1));
            Assert.True(result.StoppedOnDeferral);
            Assert.Empty(await poison.ListAsync(original.WorkflowExecutionId));
        }

        // Dispose the original context/connection and reopen the EF adapter. This checks stored queue
        // visibility; it does not substitute for the separate full native process-restart acceptance.
        await using (var restarted = database.Open("tenant-a"))
        {
            var retained = Assert.Single((await restarted.Store.ListAsync(new(original.WorkflowExecutionId))).Items);
            Assert.Equal(original.WorkItemId, retained.WorkItemId);
            Assert.Equal(original.CommandId, retained.CommandId);
            Assert.Equal(original.IdempotencyKey, retained.IdempotencyKey);
            Assert.Equal((now + delay).UtcTicks, (await restarted.Context.SchedulerWorkItems.AsNoTracking().SingleAsync()).VisibleAfterUtcTicks);
            Assert.Empty(await restarted.Store.ListClaimableWorkflowExecutionIdsAsync(new(now, 10)));
            Assert.Empty((await Drainer(restarted.Store).DrainAsync(new(original.WorkflowExecutionId, maxWorkItems: 1))).Items);
            Assert.Equal(1, handler.Calls);
            clock.Advance(delay);
            Assert.Equal(new[] { original.WorkflowExecutionId },
                await restarted.Store.ListClaimableWorkflowExecutionIdsAsync(new(clock.GetUtcNow(), 10)));
            var retry = await Drainer(restarted.Store).DrainAsync(new(original.WorkflowExecutionId, maxWorkItems: 1));
            Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(retry.Items).Status);
            Assert.Equal(2, handler.Calls);
            Assert.Empty((await restarted.Store.ListAsync(new(original.WorkflowExecutionId))).Items);
            Assert.Empty(await poison.ListAsync(original.WorkflowExecutionId));
        }

        WorkflowSchedulerDrainer Drainer(IWorkflowSchedulerWorkQueue queue) => new(
            queue, [handler], new InMemoryWorkflowExecutionStateStore(), poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock),
            clock, claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = delay });
    }

    [Fact]
    public async Task Coalescing_flush_delays_its_released_continuation_behind_a_head_another_writer_queued()
    {
        // #3690 over EF: the overlay seeds work-1 and adds the continuation work-3; while the drain owns the run, another
        // writer queues work-2 durably, which orders ahead of work-3. The flush used to claim work-2 as the head, refuse,
        // and throw after its commit; it now delays work-3 by identity and leaves work-2 claimable.
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var lease = TimeSpan.FromMinutes(1);
        var visibleAt = now.AddDays(1);
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.EnqueueAsync(Work("wf-flush", "work-1", 1));
        var session = new RuntimeCoalescingSession("wf-flush", fixture.Store, new PersistenceModeOptions());
        await session.EnqueueOverlayAsync(Work("wf-flush", "work-3", 3, WorkflowExecutionCommandKind.InvokeActivity), default);
        var source = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(new("wf-flush", "drain", now, lease), default));
        Assert.True((await session.CompleteOverlayClaimAsync(source, default)).Succeeded);
        var continuation = Assert.IsType<RuntimeSchedulerWorkClaim>(await session.ClaimOverlayAsync(new("wf-flush", "drain", now, lease), default));
        Assert.Equal("work-3", continuation.Item.WorkItemId);
        Assert.True((await session.ReleaseOverlayClaimAsync(continuation, visibleAt, default)).Succeeded);
        await fixture.Store.EnqueueAsync(Work("wf-flush", "work-2", 2));

        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);

        Assert.Equal([("work-2", (DateTimeOffset?)null), ("work-3", visibleAt)],
            (await fixture.Store.ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery("wf-flush"))).Items
            .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter)));
        var head = await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-flush", "sweep", now, lease));
        Assert.Equal("work-2", head?.Item.WorkItemId);
        Assert.True((await fixture.Store.CompleteClaimAsync(head!)).Succeeded);
        Assert.Null(await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-flush", "sweep", visibleAt - TimeSpan.FromTicks(1), lease)));
        Assert.Equal("work-3", (await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-flush", "sweep", visibleAt, lease)))?.Item.WorkItemId);
    }

    private sealed class UnavailableOnceHandler : IWorkflowSchedulerWorkHandler
    {
        public int Calls { get; private set; }
        public string Name => nameof(UnavailableOnceHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (++Calls == 1)
                throw new RuntimeSchedulerWorkUnavailableException("test.store-unavailable", "The test store is temporarily unavailable.");
            return ValueTask.CompletedTask;
        }
    }
}
