using System.Threading.Channels;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingDeliveredBackstopTests
{
    private const string ExecutionId = "delivered-backstop-run";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Durable)]
    public async Task A_lapsed_cap_flush_backstop_already_delivered_by_the_overlay_is_acknowledged_without_dispatching_again(PersistenceMode mode)
    {
        var clock = new PollClock();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var scheduler = new InMemorySchedulerStateStore();
        var probe = new Probe();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(queue);
        services.AddSingleton<ISchedulerStateStore>(scheduler);
        services.AddSingleton(new InMemoryRuntimeCheckpointStoreState());
        services.AddSingleton<IRuntimePersistenceModeResolver>(new FixedPersistenceModeResolver(mode));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowSchedulerWorkHandler>();
        services.AddSingleton(probe);
        services.AddScoped<IWorkflowSchedulerWorkHandler, Handler>();
        services.AddPersistenceModes(options => { options.Mode = mode; options.MaxActivitiesPerCommit = 1; });

        var commitDescriptor = services.Single(item => item.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>));
        services.Remove(commitDescriptor);
        services.Add(new ServiceDescriptor(typeof(CoalescingInner<IRuntimeCheckpointCommitStore>), provider =>
        {
            var inner = ((CoalescingInner<IRuntimeCheckpointCommitStore>)commitDescriptor.ImplementationFactory!(provider)).Value;
            return new CoalescingInner<IRuntimeCheckpointCommitStore>(new CaptureBackstopStore(inner, probe));
        }, commitDescriptor.Lifetime));
        var dispatcherDescriptor = services.Single(item => item.ServiceType == typeof(IRuntimePostCommitIntentDispatcher));
        services.Remove(dispatcherDescriptor);
        services.Add(new ServiceDescriptor(typeof(IRuntimePostCommitIntentDispatcher), provider =>
        {
            var original = dispatcherDescriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, dispatcherDescriptor.ImplementationType!);
            return new CountingDispatcher((IRuntimePostCommitIntentDispatcher)original, probe);
        }, dispatcherDescriptor.Lifetime));

        await using var host = services.BuildServiceProvider();
        await queue.EnqueueAsync(Work("a", WorkflowExecutionCommandKind.ScheduleActivity, 1));
        await queue.EnqueueAsync(Work("b", WorkflowExecutionCommandKind.ScheduleActivity, 2));
        await using var scope = host.CreateAsyncScope();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var command = new WorkflowExecutionCommand("drain", ExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork,
            Now, null, new Dictionary<string, string>());
        var envelope = new WorkflowExecutionCommandEnvelope("envelope", ExecutionId, command, "once",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now);
        var draining = scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(envelope, new RuntimeSchedulerDrainRequest(ExecutionId), timeout.Token).AsTask();

        while (!draining.IsCompleted)
        {
            var poll = clock.NextPollAsync(timeout.Token);
            if (await Task.WhenAny(draining, poll) == draining)
                break;
            clock.Advance(await poll);
        }

        var result = await draining;
        Assert.NotNull(probe.PeerClaim);
        Assert.True(probe.OverlayDeliveredBeforeWaiting);
        Assert.True(probe.BackstopWasPersisted);
        Assert.Equal(1, probe.ContinuationDispatches);
        Assert.Equal(1, probe.ContinuationBodies);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["a", "b", "c"], result.Items.Select(item => item.WorkItemId));
        Assert.Equal(3L, (await scheduler.FindAsync(ExecutionId))?.Version);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(ExecutionId)));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(ExecutionId));
        var physical = scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
        // Inspect the captured physical store directly: this legacy fixture has no committed source-scope witness.
        var settled = await Assert.IsAssignableFrom<IPostCommitOutboxLookupStore>(physical).FindAsync(probe.PeerClaim.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, settled?.Status);
        Assert.Equal(2, settled?.DeliveryFencingToken);
        Assert.Equal(1, settled?.DeliveryAttemptCount);
    }

    private static RuntimeSchedulerWorkItem Work(string id, WorkflowExecutionCommandKind kind, long sequence) =>
        new(id, ExecutionId, "command-" + id, kind, "envelope-" + id, "key-" + id, Now, Now, sequence);

    private sealed class Probe
    {
        public RuntimePostCommitOutboxClaim? PeerClaim { get; set; }
        public bool OverlayDeliveredBeforeWaiting { get; set; }
        public bool BackstopWasPersisted { get; set; }
        public int ContinuationDispatches;
        public int ContinuationBodies;
    }

    private sealed class Handler(RuntimeCheckpointCommitter committer, ISchedulerStateStore scheduler,
        IRuntimeCoalescingSessionAccessor sessions, Probe probe) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(Handler);
        public bool CanHandle(RuntimeSchedulerWorkItem item) => item.CommandKind is
            WorkflowExecutionCommandKind.ScheduleActivity or WorkflowExecutionCommandKind.InvokeActivity;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem item, CancellationToken cancellationToken = default)
        {
            if (item.WorkItemId == "c" && Interlocked.Increment(ref probe.ContinuationBodies) == 1)
            {
                var session = Assert.IsType<RuntimeCoalescingSession>(sessions.Current);
                Assert.NotNull(probe.PeerClaim);
                Assert.True(session.TryFindOutboxItem(probe.PeerClaim.OutboxItemId, out var delivered));
                probe.OverlayDeliveredBeforeWaiting = delivered?.Status == RuntimePostCommitOutboxStatus.Delivered;
                probe.BackstopWasPersisted = session.IsOutboxDurablyPersisted(probe.PeerClaim.OutboxItemId);
            }

            var previous = await scheduler.FindAsync(ExecutionId, cancellationToken);
            var checkpoint = new RuntimeCheckpoint("checkpoint-" + item.WorkItemId, "BackstopProbe", ExecutionId, Now,
                item.WorkItemId == "c" ? [] : ["activity-" + item.WorkItemId], new Dictionary<string, string>());
            var changes = new RuntimeCheckpointStateChangeSet(null,
                new RuntimeStateChange<SchedulerState>(ExecutionId, RuntimeStateChangeOperation.Upsert,
                    new SchedulerState(ExecutionId, (previous?.Version ?? 0) + 1), new Dictionary<string, string>()),
                [], [], [], [], []);
            var intents = item.WorkItemId == "b"
                ? new[] { SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(item, "activity-b",
                    Work("c", WorkflowExecutionCommandKind.InvokeActivity, 3), Now) }
                : [];
            await committer.CommitAsync(new RuntimeCheckpointCommit("commit-" + item.WorkItemId, checkpoint, changes,
                intents, new Dictionary<string, string>()), cancellationToken);
        }
    }

    private sealed class CaptureBackstopStore(IRuntimeCheckpointCommitStore inner, Probe probe) : IRuntimeCheckpointCommitStore
    {
        public bool SupportsModelCheckpoint => inner.SupportsModelCheckpoint;
        public bool CanCommitModelState(IRuntimeModelExecutionReader reader) => inner.CanCommitModelState(reader);
        public bool SupportsModelParentAdmission => inner.SupportsModelParentAdmission;
        public bool SupportsModelAdmission => inner.SupportsModelAdmission;
        public bool CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
            IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox) => inner.CanCommitModelAdmission(reader, executions, activities, outbox);
        public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
        public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore executions,
            IActivityExecutionStateStore activities) => inner.CanCommitExactBookmarkResume(bookmarks, executions, activities);

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            if (commit.CommitId == "commit-b")
            {
                // The real cap flush committed the continuation; claim that physical backstop before overlay delivery.
                Assert.Equal(2L, commit.StateChanges.Scheduler?.State.Version);
                Assert.Single(commit.StateChanges.PostCommitOutbox);
                probe.PeerClaim = Assert.Single(await Assert.IsAssignableFrom<IRuntimePostCommitOutboxClaimStore>(inner)
                    .ClaimAsync(new RuntimePostCommitOutboxClaimRequest("dead-deliverer", Now, TimeSpan.FromSeconds(1),
                        1, ExecutionId, RuntimePostCommitIntentKinds.EnqueueSchedulerWork), cancellationToken));
            }
            return result;
        }
    }

    private sealed class CountingDispatcher(IRuntimePostCommitIntentDispatcher inner, Probe probe) : IRuntimePostCommitIntentDispatcher
    {
        public bool RequiresDurableSource(string kind) => inner.RequiresDurableSource(kind);

        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            if (intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork)
                Interlocked.Increment(ref probe.ContinuationDispatches);
            return inner.DispatchAsync(intent, cancellationToken);
        }

        public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            if (source.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork)
                Interlocked.Increment(ref probe.ContinuationDispatches);
            return inner.DispatchAsync(source, cancellationToken);
        }
    }

    private sealed class PollClock() : FakeTimeProvider(Now)
    {
        private readonly Channel<TimeSpan> _polls = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime > TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(1))
                _polls.Writer.TryWrite(dueTime);
            return timer;
        }

        public async Task<TimeSpan> NextPollAsync(CancellationToken cancellationToken) =>
            await _polls.Reader.ReadAsync(cancellationToken);
    }
}
