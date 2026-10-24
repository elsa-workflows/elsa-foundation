using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerWorkDeferralTests
{
    private const string WorkflowExecutionId = "wf-deferral";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Claim_options_default_unavailable_retry_delay_is_bounded()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new RuntimeSchedulerWorkClaimOptions().UnavailableRetryDelay);
    }

    [Fact]
    public async Task Unavailable_dispatch_releases_same_item_until_retry_time_then_completes_without_poison()
    {
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new ObservingWorkQueue(inner);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var timeProvider = new FakeTimeProvider(Now);
        var retryDelay = TimeSpan.FromSeconds(17);
        var handler = new UnavailableOnceHandler();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler],
            timeProvider,
            poisonStore: poisonStore,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = retryDelay });
        await queue.EnqueueAsync(Work("work-source", 1));
        await queue.EnqueueAsync(Work("work-following", 2));

        var deferred = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(deferred.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, deferred.StopReason);
        var deferredItem = Assert.Single(deferred.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, deferredItem.Status);
        Assert.Equal("work-source", deferredItem.WorkItemId);
        var commandResult = WorkflowExecutionCommandProcessResult.FromDrain(deferred);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, commandResult.StopReason);
        Assert.False(commandResult.IsFaulted);
        Assert.False(commandResult.WorkflowTerminated);
        Assert.Equal(deferredItem.Error, commandResult.FaultReason);
        Assert.Equal(0, deferred.DrainedCount);
        Assert.Equal(0, queue.CompleteClaimCalls);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Equal(0, queue.DequeueCalls);
        Assert.Equal(Now + retryDelay, queue.LastReleasedVisibleAt);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
        Assert.Equal(
            new[] { "work-source", "work-following" },
            (await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items.Select(item => item.WorkItemId));
        Assert.Null(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            WorkflowExecutionId,
            "probe-before-retry",
            Now + retryDelay - TimeSpan.FromTicks(1),
            TimeSpan.FromMinutes(1))));

        timeProvider.Advance(retryDelay);
        var retried = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId, maxWorkItems: 1));

        Assert.Equal(1, retried.DrainedCount);
        Assert.False(retried.StoppedOnDeferral);
        Assert.Equal(new[] { "work-source", "work-source" }, handler.WorkItemIds);
        Assert.Equal(1, queue.CompleteClaimCalls);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
        Assert.Equal(
            new[] { "work-following" },
            (await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items.Select(item => item.WorkItemId));
    }

    [Fact]
    public async Task Unavailable_dispatch_on_legacy_queue_preserves_source_without_ack_or_poison()
    {
        var queue = new ObservingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue(), supportsClaimTransitions: false);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new AlwaysUnavailableHandler()],
            new FakeTimeProvider(Now),
            poisonStore: poisonStore);
        await queue.EnqueueAsync(Work("legacy-source", 1));

        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(0, queue.DequeueCalls);
        Assert.Equal(0, queue.CompleteClaimCalls);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Equal("legacy-source", Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items).WorkItemId);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
    }

    [Fact]
    public async Task Unsupported_command_preserves_its_claim_without_claiming_durable_deferral()
    {
        var queue = new ObservingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        var leaseDuration = TimeSpan.FromSeconds(30);
        var drainer = TestSchedulerDrainer.Create(queue, [new AlwaysUnavailableHandler()],
            new FakeTimeProvider(Now), poisonStore: poison,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = leaseDuration });
        await queue.EnqueueAsync(Work("unsupported", 1, WorkflowExecutionCommandKind.RunSchedulerWork));

        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            drainer.DrainAsync(new(WorkflowExecutionId)).AsTask());

        Assert.Equal(0, queue.CompleteClaimCalls);
        Assert.Equal(0, queue.ReleaseClaimCalls);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Empty(await poison.ListAsync(WorkflowExecutionId));
        Assert.Equal("unsupported", Assert.Single((await queue.ListAsync(new(WorkflowExecutionId))).Items).WorkItemId);
        Assert.Null(await queue.ClaimAsync(new(WorkflowExecutionId, "successor", Now, leaseDuration)));
        var recovered = await queue.ClaimAsync(new(WorkflowExecutionId, "successor", Now + leaseDuration, leaseDuration));
        Assert.Equal("unsupported", recovered!.Item.WorkItemId);
    }

    [Fact]
    public async Task Stale_unavailable_release_cannot_change_successor_claim_or_poison_source()
    {
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new ObservingWorkQueue(inner);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new SuccessorClaimingUnavailableHandler(inner);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler],
            new FakeTimeProvider(Now),
            poisonStore: poisonStore,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = TimeSpan.FromSeconds(13) });
        await queue.EnqueueAsync(Work("stale-release-source", 1));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, queue.LastReleaseStatus);
        Assert.Equal(0, queue.CompleteClaimCalls);
        Assert.Equal(0, queue.ConsumeClaimedCalls);
        Assert.Equal("stale-release-source", Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items).WorkItemId);
        Assert.Empty(await poisonStore.ListAsync(WorkflowExecutionId));
        Assert.NotNull(handler.SuccessorClaim);
        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await inner.CompleteClaimAsync(handler.SuccessorClaim!)).Status);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
    }

    [Fact]
    public async Task Ordinary_handler_failure_still_acknowledges_and_records_poison()
    {
        var queue = new ObservingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new FaultingHandler()],
            new FakeTimeProvider(Now),
            poisonStore: poisonStore);
        await queue.EnqueueAsync(Work("ordinary-failure", 1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.True(result.StoppedOnFault);
        Assert.False(result.StoppedOnDeferral);
        Assert.Equal(1, queue.CompleteClaimCalls);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        var poison = Assert.Single(await poisonStore.ListAsync(WorkflowExecutionId));
        Assert.Equal("ordinary-failure", poison.WorkItemId);
        Assert.Equal(1, poison.FailureCount);
    }

    [Fact]
    public async Task Retained_attempt_claim_commit_keeps_source_then_later_effect_commit_consumes_it()
    {
        var queue = new ObservingWorkQueue(new InMemoryWorkflowSchedulerWorkQueue());
        var accessor = new RuntimeConsumedSchedulerWorkClaimAccessor();
        var store = new InMemoryRuntimeCheckpointCommitStore(schedulerWorkQueue: queue);
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateCheckpointPersistencePolicy(),
            store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            enrichers: [],
            intentHandlerContributions: [],
            consumedWorkClaimAccessor: accessor);
        await queue.EnqueueAsync(Work("work-retained", 1));
        var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            WorkflowExecutionId,
            "test-owner",
            Now,
            TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);

        using (accessor.Begin(ConsumedSchedulerWorkItem.FromClaim(claim!)))
        {
            await committer.CommitAsync(Commit("attempt-claim", RuntimeCheckpointNames.ActivityAttemptClaimed, retainSchedulerWork: true));

            Assert.False(accessor.WasConsumedDurably);
            Assert.Equal(0, queue.ConsumeClaimedCalls);
            Assert.Equal("work-retained", Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items).WorkItemId);

            await committer.CommitAsync(Commit("activity-completed", RuntimeCheckpointNames.ActivityCompleted));

            Assert.True(accessor.WasConsumedDurably);
            Assert.Equal(1, queue.ConsumeClaimedCalls);
            Assert.Equal(0, queue.CompleteClaimCalls);
            Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Items);
        }
    }

    private static RuntimeSchedulerWorkItem Work(string workItemId, long sequence, WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.ResumeBookmark) =>
        new(
            workItemId,
            WorkflowExecutionId,
            $"command-{workItemId}",
            kind,
            $"envelope-{workItemId}",
            $"{WorkflowExecutionId}:{workItemId}",
            Now,
            Now,
            sequence);

    private static RuntimeCheckpointCommit Commit(string commitId, string checkpointName, bool retainSchedulerWork = false) =>
        new(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-{commitId}",
                Name: checkpointName,
                WorkflowExecutionId: WorkflowExecutionId,
                OccurredAt: Now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>())
        {
            RetainSchedulerWork = retainSchedulerWork
        };

    private sealed class UnavailableOnceHandler : IWorkflowSchedulerWorkHandler
    {
        private int _calls;

        public string Name => nameof(UnavailableOnceHandler);
        public List<string> WorkItemIds { get; } = [];
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            WorkItemIds.Add(workItem.WorkItemId);
            if (Interlocked.Increment(ref _calls) == 1)
                throw new RuntimeSchedulerWorkUnavailableException("test.unavailable", "The test dependency is unavailable.");

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysUnavailableHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AlwaysUnavailableHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new RuntimeSchedulerWorkUnavailableException("test.unavailable", "The test dependency is unavailable.");
    }

    private sealed class SuccessorClaimingUnavailableHandler(InMemoryWorkflowSchedulerWorkQueue queue) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(SuccessorClaimingUnavailableHandler);
        public RuntimeSchedulerWorkClaim? SuccessorClaim { get; private set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            var current = Assert.Single(await queue.ListActiveClaimsAsync(WorkflowExecutionId, Now));
            SuccessorClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                WorkflowExecutionId,
                "successor-owner",
                current.VisibleAfter,
                TimeSpan.FromMinutes(1)));
            Assert.NotNull(SuccessorClaim);
            throw new RuntimeSchedulerWorkUnavailableException("test.unavailable", "The test dependency is unavailable.");
        }
    }

    private sealed class FaultingHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FaultingHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ordinary handler failure");
    }

    private sealed class ImmediateCheckpointPersistencePolicy : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(
            RuntimeCheckpoint checkpoint,
            CancellationToken cancellationToken = default) =>
            new(new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate, "test"));
    }

    private sealed class ObservingWorkQueue(
        IWorkflowSchedulerWorkQueue inner,
        bool supportsClaimTransitions = true) : IWorkflowSchedulerWorkQueue
    {
        public bool SupportsClaimTransitions => supportsClaimTransitions && inner.SupportsClaimTransitions;
        public bool SupportsTargetedDeletion => inner.SupportsTargetedDeletion;
        public int DequeueCalls { get; private set; }
        public int CompleteClaimCalls { get; private set; }
        public int ConsumeClaimedCalls { get; private set; }
        public int ReleaseClaimCalls { get; private set; }
        public DateTimeOffset? LastReleasedVisibleAt { get; private set; }
        public RuntimeSchedulerWorkClaimTransitionStatus? LastReleaseStatus { get; private set; }

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            inner.EnqueueAsync(workItem, cancellationToken);

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            DequeueCalls++;
            return inner.DequeueAsync(workflowExecutionId, cancellationToken);
        }

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListClaimableWorkflowExecutionIdsAsync(
            RuntimeSchedulerClaimableBacklogQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListClaimableWorkflowExecutionIdsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyDictionary<string, RuntimeSchedulerWorkItem>> ListNextWorkItemsAsync(
            IReadOnlyCollection<string> workflowExecutionIds,
            CancellationToken cancellationToken = default) =>
            inner.ListNextWorkItemsAsync(workflowExecutionIds, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default) =>
            inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            CancellationToken cancellationToken = default)
        {
            CompleteClaimCalls++;
            return inner.CompleteClaimAsync(claim, cancellationToken);
        }

        public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
            RuntimeSchedulerWorkClaim claim,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken = default)
        {
            ReleaseClaimCalls++;
            LastReleasedVisibleAt = visibleAt;
            var result = await inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
            LastReleaseStatus = result.Status;
            return result;
        }

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
            ConsumedSchedulerWorkItem consumed,
            CancellationToken cancellationToken = default)
        {
            ConsumeClaimedCalls++;
            return inner.ConsumeClaimedAsync(consumed, cancellationToken);
        }
    }
}
