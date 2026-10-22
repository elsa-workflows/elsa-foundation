using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingResumeDurableClaimBoundaryTests
{
    private const string WorkflowExecutionId = "wf-coalesced-resume-boundary";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(17);

    [Fact]
    public async Task Resume_boundary_flushes_coalesced_work_then_durable_deferral_survives_provider_rebuild()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: true);
        await using var firstProvider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 3));

        RuntimeSchedulerDrainResult deferred;
        await using (var scope = firstProvider.CreateAsyncScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>();
            deferred = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        }

        Assert.True(deferred.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, deferred.StopReason);
        Assert.Equal(
            ["schedule-1", "schedule-2", "resume-1"],
            deferred.Items.Select(item => item.WorkItemId));
        Assert.Equal(
            [
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Deferred
            ],
            deferred.Items.Select(item => item.Status));

        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var resumeAtDeferral = Assert.Single(probe.ResumeSnapshots);
        Assert.Equal(2L, resumeAtDeferral.SchedulerVersion);
        Assert.Equal(1, resumeAtDeferral.DurableCommitCount);
        Assert.False(resumeAtDeferral.SessionActive);
        Assert.Equal(["resume-1"], resumeAtDeferral.ActiveClaimWorkItemIds);

        await using (var auditScope = firstProvider.CreateAsyncScope())
        {
            var durableFlush = Assert.Single(InnerCommitStore(auditScope.ServiceProvider).ListCommits());
            Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, durableFlush.Commit.Checkpoint.Name);
            Assert.Equal(2L, durableFlush.Commit.StateChanges.Scheduler?.State.Version);
            Assert.Empty(await auditScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                .ListAsync(WorkflowExecutionId));
        }
        Assert.Equal("resume-1", Assert.Single(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).WorkItemId);

        // A newly built provider sees the same queue row and its future visibility. The early read is through the
        // coalescing decorator with no active session, so it exercises the durable provider path used after restart.
        await using var reopenedProvider = BuildProvider(shared, probe);
        await using var reopenedScope = reopenedProvider.CreateAsyncScope();
        var reopenedQueue = reopenedScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var notYetVisibleAt = Now + RetryDelay - TimeSpan.FromTicks(1);
        Assert.Empty(await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(notYetVisibleAt)));

        shared.TimeProvider.Advance(RetryDelay);
        Assert.Equal(
            [WorkflowExecutionId],
            await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now + RetryDelay)));

        var retried = await reopenedScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.False(retried.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, retried.StopReason);
        Assert.Equal("resume-1", Assert.Single(retried.Items).WorkItemId);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(retried.Items).Status);
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        Assert.All(probe.ResumeSnapshots, snapshot =>
        {
            Assert.Equal(2L, snapshot.SchedulerVersion);
            Assert.Equal(1, snapshot.DurableCommitCount);
            Assert.False(snapshot.SessionActive);
            Assert.Equal(["resume-1"], snapshot.ActiveClaimWorkItemIds);
        });
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Ordinary_schedule_work_remains_coalesced_until_drain_quiescence()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(2, result.DrainedCount);
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var folded = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, folded.Commit.Checkpoint.Name);
        Assert.Equal(2L, folded.Commit.StateChanges.Scheduler?.State.Version);
        Assert.Equal(2L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    private static ServiceProvider BuildProvider(SharedPersistence shared, ProbeRecorder probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(shared.TimeProvider);
        services.AddSingleton(new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = RetryDelay });
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(shared.Queue);
        services.AddSingleton<ISchedulerStateStore>(shared.SchedulerStateStore);
        services.AddSingleton(shared.CommitState);
        services.AddSingleton<IRuntimePersistenceModeResolver>(new FixedPersistenceModeResolver(PersistenceMode.Balanced));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowSchedulerWorkHandler>();
        services.AddSingleton(probe);
        services.AddScoped<IWorkflowSchedulerWorkHandler, CoalescingProbeHandler>();
        services.AddPersistenceModes();
        return services.BuildServiceProvider();
    }

    private static InMemoryRuntimeCheckpointCommitStore InnerCommitStore(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value as InMemoryRuntimeCheckpointCommitStore ??
        throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

    private static RuntimeSchedulerWorkItem ScheduleWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ScheduleActivity,
        sequence);

    private static RuntimeSchedulerWorkItem ResumeWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ResumeBookmark,
        sequence);

    private static RuntimeSchedulerWorkItem Work(string workItemId, WorkflowExecutionCommandKind kind, long sequence) =>
        new(
            workItemId,
            WorkflowExecutionId,
            $"command-{workItemId}",
            kind,
            $"envelope-{workItemId}",
            $"{WorkflowExecutionId}:{workItemId}",
            Now,
            Now,
            sequence,
            commandMetadata: new Dictionary<string, string>());

    private static WorkflowExecutionCommandEnvelope Envelope()
    {
        var command = new WorkflowExecutionCommand(
            "drain-command",
            WorkflowExecutionId,
            WorkflowExecutionCommandKind.RunSchedulerWork,
            Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            "drain-envelope",
            WorkflowExecutionId,
            command,
            "drain-idempotency",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now);
    }

    private sealed class SharedPersistence(FakeTimeProvider timeProvider)
    {
        public FakeTimeProvider TimeProvider { get; } = timeProvider;
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        public InMemorySchedulerStateStore SchedulerStateStore { get; } = new();
        public InMemoryRuntimeCheckpointStoreState CommitState { get; } = new();
    }

    private sealed class ProbeRecorder(bool deferFirstResume)
    {
        private bool _didDefer;

        public List<long?> ScheduleObservedVersions { get; } = [];
        public List<int> ScheduleDurableCommitCounts { get; } = [];
        public List<ResumeSnapshot> ResumeSnapshots { get; } = [];

        public bool ShouldDeferResume()
        {
            if (!deferFirstResume || _didDefer)
                return false;

            _didDefer = true;
            return true;
        }
    }

    private sealed record ResumeSnapshot(
        long? SchedulerVersion,
        int DurableCommitCount,
        bool SessionActive,
        IReadOnlyCollection<string> ActiveClaimWorkItemIds);

    private sealed class CoalescingProbeHandler(
        RuntimeCheckpointCommitter committer,
        ISchedulerStateStore schedulerStateStore,
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
        CoalescingInner<IRuntimeCheckpointCommitStore> innerCommitStore,
        TimeProvider timeProvider,
        ProbeRecorder probe) : IWorkflowSchedulerWorkHandler
    {
        private readonly InMemoryRuntimeCheckpointCommitStore _commits =
            innerCommitStore.Value as InMemoryRuntimeCheckpointCommitStore ??
            throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

        public string Name => nameof(CoalescingProbeHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
            workItem.CommandKind is WorkflowExecutionCommandKind.ScheduleActivity or WorkflowExecutionCommandKind.ResumeBookmark;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
            {
                var current = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
                probe.ScheduleObservedVersions.Add(current?.Version);
                var nextState = new SchedulerState(workItem.WorkflowExecutionId, (current?.Version ?? 0) + 1);
                await committer.CommitAsync(Commit(workItem, nextState), cancellationToken);
                probe.ScheduleDurableCommitCounts.Add(_commits.ListCommits().Count);
                return;
            }

            var persistedState = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
            var activeClaims = await (innerQueue.Value as IWorkflowSchedulerWorkClaimInspection ??
                                      throw new InvalidOperationException("The coalescing probe requires claim inspection."))
                .ListActiveClaimsAsync(workItem.WorkflowExecutionId, timeProvider.GetUtcNow(), cancellationToken);
            probe.ResumeSnapshots.Add(new ResumeSnapshot(
                persistedState?.Version,
                _commits.ListCommits().Count,
                sessionAccessor.Current?.IsActive ?? false,
                activeClaims.Select(claim => claim.Item.WorkItemId).ToArray()));

            if (probe.ShouldDeferResume())
                throw new RuntimeSchedulerWorkUnavailableException(
                    "probe.resume-unavailable",
                    "The resume dependency is temporarily unavailable.");
        }

        private static RuntimeCheckpointCommit Commit(RuntimeSchedulerWorkItem workItem, SchedulerState state)
        {
            var checkpoint = new RuntimeCheckpoint(
                $"checkpoint-{workItem.WorkItemId}",
                "CoalescingProbe",
                workItem.WorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>());
            var changes = new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: new RuntimeStateChange<SchedulerState>(
                    workItem.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>()),
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []);
            return new RuntimeCheckpointCommit(
                $"commit-{workItem.WorkItemId}",
                checkpoint,
                changes,
                [],
                new Dictionary<string, string>());
        }
    }
}
