using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
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
