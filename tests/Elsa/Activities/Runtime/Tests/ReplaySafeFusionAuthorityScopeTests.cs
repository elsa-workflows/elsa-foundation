using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Path-A source-domain guardrails that distinguish a true D2-to-nested-D1 pump dispatch from a resumption barrier.
/// The exact dispatcher is observed through the test-decorated ScheduleActivity handler and the existing scoped
/// consumed-claim accessor; authority itself is read by reflection so the RED tests remain source-domain owned.
/// </summary>
public sealed class ReplaySafeFusionAuthorityScopeTests
{
    private const string OuterNodeId = "node-flowchart";
    private const string FirstSuccessorNodeId = "node-first";
    private const string NestedSuccessorNodeId = "node-nested";
    private static readonly string[] PreparationNames =
    [
        RuntimeCheckpointNames.ActivityScheduled,
        RuntimeCheckpointNames.ActivityStarted,
        RuntimeCheckpointNames.ActivityAttemptClaimed
    ];

    [Fact]
    public async Task Nested_D2_to_D1_pump_dispatch_inherits_the_outer_durable_schedule_authority()
    {
        var observations = new AuthorityObservationSink();
        var dispatches = new ScheduleActivityDispatchObservationSink();
        var drainObservations = new ResumptionDrainObservationSink();
        var boundaryCrash = new ArmedPostCommitBoundaryCrash();
        var physicalFolds = new PhysicalFoldObserver();
        await using var harness = CreateHarness(observations, dispatches, drainObservations, boundaryCrash, physicalFolds);

        var outerScheduleWorkItem = await StageOuterLeafScheduleAsync(harness, boundaryCrash);
        var diagnostics = harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>();
        observations.Clear();
        dispatches.Clear();
        drainObservations.Clear();
        diagnostics.Reset();
        boundaryCrash.Disarm();

        var workQueue = harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var stagedQueue = await workQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
        var resumptionBarrier = Assert.Single(stagedQueue, item =>
            item.CommandKind == WorkflowExecutionCommandKind.RunSchedulerWork);
        Assert.True(await workQueue.DeleteAsync(
            WorkflowExecutionHarness.WorkflowExecutionId,
            resumptionBarrier.WorkItemId));
        var queuedBeforeDrain = await workQueue.ListAllAsync(
            new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Collection(queuedBeforeDrain, item => Assert.Equal(outerScheduleWorkItem.WorkItemId, item.WorkItemId));

        var drainResult = await harness.Services.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(
                NewEnvelope(outerScheduleWorkItem),
                new RuntimeSchedulerDrainRequest(WorkflowExecutionHarness.WorkflowExecutionId));
        var workflowState = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        var isolatedDrain = Assert.Single(drainObservations.Snapshot());
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, isolatedDrain.Envelope.Command.Kind);
        Assert.DoesNotContain(drainResult.Items, item =>
            item.CommandKind == WorkflowExecutionCommandKind.RunSchedulerWork);
        var outerDrainItem = Assert.Single(drainResult.Items, item =>
            item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerDrainItem.WorkItemId);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, outerDrainItem.CommandKind);
        Assert.True(diagnostics.FusedSpans >= 2,
            $"Expected the outer leaf and its nested ReplaySafe successor to fuse, saw {diagnostics.FusedSpans} spans.");
        Assert.True(diagnostics.InlineCascadeDispatches > 0,
            $"Expected the real D2 completion pump to dispatch nested work inline, saw {diagnostics.InlineCascadeDispatches} dispatches.");

        // T028 RED: a non-zero fold count alone can be a one-member compatibility flush. The durable scheduler
        // continuation handoff needs a real multi-member provider fold before the D2 completion pump is accepted as
        // preserving the outer D1 authority. T029 owns the production behavior that makes this true.
        Assert.True(physicalFolds.MaximumMemberCount > 1,
            $"Expected a physical prepared fold with more than one member, saw {physicalFolds.MaximumMemberCount}.");

        var scheduleDispatches = dispatches.Snapshot();
        var outerDispatch = Assert.Single(scheduleDispatches, item => item.ExecutableNodeId == FirstSuccessorNodeId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerDispatch.WorkItemId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerDispatch.PendingConsumeWorkItemId);
        var nestedPumpDispatch = Assert.Single(scheduleDispatches, item => item.ExecutableNodeId == NestedSuccessorNodeId);
        Assert.NotEqual(outerScheduleWorkItem.WorkItemId, nestedPumpDispatch.WorkItemId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, nestedPumpDispatch.PendingConsumeWorkItemId);
        Assert.DoesNotContain(drainResult.Items, item => item.WorkItemId == nestedPumpDispatch.WorkItemId);

        var relevant = AssertPreparationMatrix(observations);
        AssertAuthorityAccessorAvailable(observations);

        var outerScheduled = Assert.Single(relevant, item =>
            item.ExecutableNodeId == FirstSuccessorNodeId && item.CheckpointName == RuntimeCheckpointNames.ActivityScheduled);
        var outerAuthority = Assert.IsType<AuthoritySnapshot>(outerScheduled.Authority);
        Assert.Equal(1, outerAuthority.Version);
        Assert.Equal("runtime.scheduler-work", outerAuthority.Kind);
        Assert.Equal(WorkflowExecutionHarness.WorkflowExecutionId, outerAuthority.WorkflowExecutionId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerScheduled.SchedulerWorkItemId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerAuthority.WorkItemId);
        Assert.Matches("^sha256:[0-9a-f]{64}$", outerAuthority.Fingerprint);

        var nestedScheduled = Assert.Single(relevant, item =>
            item.ExecutableNodeId == NestedSuccessorNodeId && item.CheckpointName == RuntimeCheckpointNames.ActivityScheduled);
        Assert.Equal(nestedPumpDispatch.WorkItemId, nestedScheduled.SchedulerWorkItemId);

        AssertPreparationsUseAuthority(relevant, FirstSuccessorNodeId, outerAuthority);
        AssertPreparationsUseAuthority(relevant, NestedSuccessorNodeId, outerAuthority);
    }

    [Fact]
    public async Task Resumption_barrier_drains_RunSchedulerWork_before_later_durable_schedule_without_authority_leakage()
    {
        var observations = new AuthorityObservationSink();
        var dispatches = new ScheduleActivityDispatchObservationSink();
        var drainObservations = new ResumptionDrainObservationSink();
        var boundaryCrash = new ArmedPostCommitBoundaryCrash();
        var physicalFolds = new PhysicalFoldObserver();
        await using var harness = CreateHarness(observations, dispatches, drainObservations, boundaryCrash, physicalFolds);

        var outerScheduleWorkItem = await StageOuterLeafScheduleAsync(harness, boundaryCrash);
        var diagnostics = harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>();
        observations.Clear();
        dispatches.Clear();
        drainObservations.Clear();
        diagnostics.Reset();
        boundaryCrash.Disarm();

        await ResolveResumptionService(harness.Services).SweepAsync(new RuntimeResumptionSweepRequest());
        var workflowState = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        var resumption = Assert.Single(drainObservations.Snapshot());
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, resumption.Envelope.Command.Kind);
        Assert.Equal(
            [WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionCommandKind.ScheduleActivity],
            resumption.Result.Items.Take(2).Select(item => item.CommandKind).ToArray());

        var scheduleDispatches = dispatches.Snapshot();
        var resumedOuter = Assert.Single(scheduleDispatches, item => item.ExecutableNodeId == FirstSuccessorNodeId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, resumedOuter.WorkItemId);
        Assert.Equal(resumedOuter.WorkItemId, resumedOuter.PendingConsumeWorkItemId);
        var laterDurable = Assert.Single(scheduleDispatches, item => item.ExecutableNodeId == NestedSuccessorNodeId);
        Assert.Equal(laterDurable.WorkItemId, laterDurable.PendingConsumeWorkItemId);
        Assert.NotEqual(resumedOuter.PendingConsumeWorkItemId, laterDurable.PendingConsumeWorkItemId);

        var relevant = AssertPreparationMatrix(observations);
        AssertAuthorityAccessorAvailable(observations);
        var outerAuthority = Assert.IsType<AuthoritySnapshot>(Assert.Single(relevant, item =>
            item.ExecutableNodeId == FirstSuccessorNodeId &&
            item.CheckpointName == RuntimeCheckpointNames.ActivityScheduled).Authority);
        var laterAuthority = Assert.IsType<AuthoritySnapshot>(Assert.Single(relevant, item =>
            item.ExecutableNodeId == NestedSuccessorNodeId &&
            item.CheckpointName == RuntimeCheckpointNames.ActivityScheduled).Authority);
        Assert.Equal(resumedOuter.WorkItemId, outerAuthority.WorkItemId);
        Assert.Equal(laterDurable.WorkItemId, laterAuthority.WorkItemId);
        Assert.NotEqual(outerAuthority, laterAuthority);

        AssertPreparationsUseAuthority(relevant, FirstSuccessorNodeId, outerAuthority);
        AssertPreparationsUseAuthority(relevant, NestedSuccessorNodeId, laterAuthority);
    }

    private static WorkflowExecutionHarness CreateHarness(
        AuthorityObservationSink observations,
        ScheduleActivityDispatchObservationSink dispatches,
        ResumptionDrainObservationSink drainObservations,
        ArmedPostCommitBoundaryCrash boundaryCrash,
        PhysicalFoldObserver physicalFolds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithCoalescing()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeReplaySafeFusionOptions
                {
                    Enabled = true,
                    FuseCompletionCascade = true
                });
                services.AddSingleton(observations);
                services.AddSingleton(dispatches);
                services.AddSingleton<IWorkflowSchedulerDrainObserver>(drainObservations);
                services.AddScoped<IRuntimeCheckpointCommitEnricher, AmbientAuthorityObservingEnricher>();
                DecorateScheduleActivityHandler(services);
                DecorateCheckpointStoreWithBoundaryCrash(services, boundaryCrash, physicalFolds);
                CoalescingDurableCheckpointStoreTestDecorator.Decorate(
                    services,
                    inner => new PreparedFoldObservingCheckpointStore(
                        (IRuntimeCheckpointPreparedLedgerStore)inner,
                        beforeFold: physicalFolds.Record));
            })
            .Build(Enumerable.Range(0, 12).Select(index => $"actexec-{index}"));

    private static AuthorityObservation[] AssertPreparationMatrix(AuthorityObservationSink observations)
    {
        var relevant = observations.Snapshot()
            .Where(item => item.ExecutableNodeId is FirstSuccessorNodeId or NestedSuccessorNodeId)
            .Where(item => PreparationNames.Contains(item.CheckpointName, StringComparer.Ordinal))
            .ToArray();
        foreach (var nodeId in new[] { FirstSuccessorNodeId, NestedSuccessorNodeId })
        foreach (var checkpointName in PreparationNames)
            Assert.Single(relevant, item =>
                StringComparer.Ordinal.Equals(item.ExecutableNodeId, nodeId) &&
                StringComparer.Ordinal.Equals(item.CheckpointName, checkpointName));
        return relevant;
    }

    private static void AssertAuthorityAccessorAvailable(AuthorityObservationSink observations)
    {
        Assert.True(observations.AccessorContractFound,
            "Missing Elsa.Workflows.Runtime.Core.Contracts.IRuntimeCheckpointRecoveryAuthorityAccessor.");
        Assert.True(observations.AccessorRegistrationFound,
            "IRuntimeCheckpointRecoveryAuthorityAccessor exists but is not registered in the active runtime scope.");
        Assert.Null(observations.ReflectionFailure);
    }

    private static void AssertPreparationsUseAuthority(
        IEnumerable<AuthorityObservation> observations,
        string executableNodeId,
        AuthoritySnapshot expectedAuthority)
    {
        foreach (var checkpointName in PreparationNames)
        {
            var observation = Assert.Single(observations, item =>
                item.ExecutableNodeId == executableNodeId &&
                item.CheckpointName == checkpointName);
            Assert.Equal(expectedAuthority, Assert.IsType<AuthoritySnapshot>(observation.Authority));
        }
    }

    private static void DecorateScheduleActivityHandler(IServiceCollection services)
    {
        var index = services.IndexOf(services.Last(item =>
            item.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            item.ImplementationType == typeof(WorkflowScheduleActivitySchedulerWorkHandler)));
        var descriptor = services[index];
        services[index] = new ServiceDescriptor(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new ScheduleActivityDispatchObservingHandler(
                (IWorkflowSchedulerWorkHandler)ActivatorUtilities.CreateInstance(
                    serviceProvider,
                    descriptor.ImplementationType!),
                serviceProvider.GetRequiredService<IRuntimeConsumedSchedulerWorkClaimAccessor>(),
                serviceProvider.GetRequiredService<ScheduleActivityDispatchObservationSink>()),
            descriptor.Lifetime);
    }

    private static void DecorateCheckpointStoreWithBoundaryCrash(
        IServiceCollection services,
        ArmedPostCommitBoundaryCrash boundaryCrash,
        PhysicalFoldObserver physicalFolds)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider => new PostCommitBoundaryCrashStore(
                (IRuntimeCheckpointCommitStore)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(serviceProvider)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!)),
                boundaryCrash,
                physicalFolds),
            descriptor.Lifetime));
    }

    private static async Task<RuntimeSchedulerWorkItem> StageOuterLeafScheduleAsync(
        WorkflowExecutionHarness harness,
        ArmedPostCommitBoundaryCrash boundaryCrash)
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.RunAsync(BuildStraightLineExecutable()));

        for (var pass = 0; pass < 8; pass++)
        {
            await DeliverPendingOutboxAsync(harness.Services);
            if (await FindOuterLeafScheduleAsync(harness.Services) is { } staged)
            {
                // Normal outbox redrive is idempotent: processing the same durable delivery surface again must not
                // create a second ScheduleActivity work item before the D2 pump takes over.
                await DeliverPendingOutboxAsync(harness.Services);
                var pending = await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
                Assert.Single(pending, item =>
                    item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
                    item.Payload is { } payload &&
                    StringComparer.Ordinal.Equals(
                        payload.Deserialize<RuntimeScheduleActivityCommandPayload>()?.ExecutableNodeId,
                        FirstSuccessorNodeId));
                return staged;
            }

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ResolveResumptionService(harness.Services)
                    .SweepAsync(new RuntimeResumptionSweepRequest()).AsTask());
        }

        boundaryCrash.Disarm();
        throw new InvalidOperationException("Failed to stage node-first as the next durable ScheduleActivity source.");
    }

    private static async Task DeliverPendingOutboxAsync(IServiceProvider services) =>
        await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(
                limit: 100,
                workflowExecutionId: WorkflowExecutionHarness.WorkflowExecutionId));

    private static async Task<RuntimeSchedulerWorkItem?> FindOuterLeafScheduleAsync(IServiceProvider services)
    {
        var pending = await services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
        return pending.SingleOrDefault(item =>
            item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
            item.Payload is { } payload &&
            StringComparer.Ordinal.Equals(
                payload.Deserialize<RuntimeScheduleActivityCommandPayload>()?.ExecutableNodeId,
                FirstSuccessorNodeId));
    }

    private static RuntimeResumptionService ResolveResumptionService(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>());

    private static WorkflowExecutionCommandEnvelope NewEnvelope(RuntimeSchedulerWorkItem workItem) =>
        new(
            envelopeId: workItem.EnvelopeId,
            workflowExecutionId: workItem.WorkflowExecutionId,
            command: new WorkflowExecutionCommand(
                workItem.CommandId,
                workItem.WorkflowExecutionId,
                workItem.CommandKind,
                workItem.EnqueuedAt,
                workItem.Payload,
                workItem.CommandMetadata),
            idempotencyKey: workItem.IdempotencyKey,
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: workItem.EnqueuedAt,
            sequence: workItem.Sequence,
            metadata: workItem.EnvelopeMetadata);

    private static WorkflowExecutable BuildStraightLineExecutable()
    {
        var first = WorkflowExecutionHarness.NewReplaySafeProbeNode(FirstSuccessorNodeId);
        var nested = WorkflowExecutionHarness.NewReplaySafeProbeNode(NestedSuccessorNodeId);
        var root = new ExecutableNode(
            executableNodeId: OuterNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [first, nested])],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                    connections:
                    [
                        new FlowchartConnection(
                            new FlowchartEndpoint(FirstSuccessorNodeId),
                            new FlowchartEndpoint(NestedSuccessorNodeId))
                    ],
                    startNodeId: FirstSuccessorNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed class AmbientAuthorityObservingEnricher(
        IServiceProvider services,
        AuthorityObservationSink sink) : IRuntimeCheckpointCommitEnricher
    {
        private const string AccessorContractName =
            "Elsa.Workflows.Runtime.Core.Contracts.IRuntimeCheckpointRecoveryAuthorityAccessor";

        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default)
        {
            var contract = typeof(IRuntimeCheckpointCommitEnricher).Assembly.GetType(AccessorContractName, throwOnError: false);
            sink.NoteContract(contract is not null);

            object? authority = null;
            if (contract is not null)
            {
                var accessor = services.GetService(contract);
                sink.NoteRegistration(accessor is not null);
                if (accessor is not null)
                {
                    try
                    {
                        var current = contract.GetProperty("Current")?.GetValue(accessor);
                        if (current is not null)
                        {
                            authority = new AuthoritySnapshot(
                                Read<int>(current, "Version"),
                                Read<string>(current, "Kind"),
                                Read<string>(current, "WorkflowExecutionId"),
                                Read<string>(current, "WorkItemId"),
                                Read<string>(current, "Fingerprint"));
                        }
                    }
                    catch (Exception exception)
                    {
                        sink.NoteReflectionFailure(exception);
                    }
                }
            }

            commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var executableNodeId);
            commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.SchedulerWorkItemId, out var schedulerWorkItemId);
            sink.Add(new AuthorityObservation(
                commit.Checkpoint.Name,
                executableNodeId,
                schedulerWorkItemId,
                authority as AuthoritySnapshot));
            return ValueTask.FromResult(commit);
        }

        private static T Read<T>(object value, string propertyName) =>
            (T)(value.GetType().GetProperty(propertyName)?.GetValue(value)
                ?? throw new InvalidOperationException($"Recovery authority is missing '{propertyName}'."));
    }

    private sealed class AuthorityObservationSink
    {
        private readonly object _gate = new();
        private readonly List<AuthorityObservation> _observations = [];

        public bool AccessorContractFound { get; private set; }
        public bool AccessorRegistrationFound { get; private set; }
        public Exception? ReflectionFailure { get; private set; }

        public void NoteContract(bool found)
        {
            lock (_gate)
                AccessorContractFound |= found;
        }

        public void NoteRegistration(bool found)
        {
            lock (_gate)
                AccessorRegistrationFound |= found;
        }

        public void NoteReflectionFailure(Exception exception)
        {
            lock (_gate)
                ReflectionFailure ??= exception;
        }

        public void Add(AuthorityObservation observation)
        {
            lock (_gate)
                _observations.Add(observation);
        }

        public void Clear()
        {
            lock (_gate)
            {
                _observations.Clear();
                AccessorContractFound = false;
                AccessorRegistrationFound = false;
                ReflectionFailure = null;
            }
        }

        public AuthorityObservation[] Snapshot()
        {
            lock (_gate)
                return _observations.ToArray();
        }
    }

    private sealed class ScheduleActivityDispatchObservingHandler(
        IWorkflowSchedulerWorkHandler inner,
        IRuntimeConsumedSchedulerWorkClaimAccessor consumedClaimAccessor,
        ScheduleActivityDispatchObservationSink sink) :
        IWorkflowSchedulerWorkHandler,
        IRuntimePipelineWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public async ValueTask HandleAsync(
            RuntimeSchedulerWorkItem workItem,
            CancellationToken cancellationToken = default)
        {
            Observe(workItem);
            await inner.HandleAsync(workItem, cancellationToken);
        }

        public async ValueTask HandleAsync(
            RuntimeSchedulerWorkItem workItem,
            IRuntimePipelineContext pipelineContext,
            CancellationToken cancellationToken = default)
        {
            Observe(workItem);
            if (inner is IRuntimePipelineWorkHandler pipelineHandler)
                await pipelineHandler.HandleAsync(workItem, pipelineContext, cancellationToken);
            else
                await inner.HandleAsync(workItem, cancellationToken);
        }

        private void Observe(RuntimeSchedulerWorkItem workItem)
        {
            var executableNodeId = workItem.Payload is { } payload
                ? payload.Deserialize<RuntimeScheduleActivityCommandPayload>()?.ExecutableNodeId
                : null;
            sink.Add(new ScheduleActivityDispatchObservation(
                workItem.WorkItemId,
                executableNodeId,
                consumedClaimAccessor.PendingConsume?.WorkItemId));
        }
    }

    private sealed class ScheduleActivityDispatchObservationSink
    {
        private readonly object _gate = new();
        private readonly List<ScheduleActivityDispatchObservation> _observations = [];

        public void Add(ScheduleActivityDispatchObservation observation)
        {
            lock (_gate)
                _observations.Add(observation);
        }

        public void Clear()
        {
            lock (_gate)
                _observations.Clear();
        }

        public ScheduleActivityDispatchObservation[] Snapshot()
        {
            lock (_gate)
                return _observations.ToArray();
        }
    }

    private sealed class ResumptionDrainObservationSink : IWorkflowSchedulerDrainObserver
    {
        private readonly object _gate = new();
        private readonly List<ResumptionDrainObservation> _observations = [];

        public ValueTask OnDrainedAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
                _observations.Add(new ResumptionDrainObservation(envelope, result));
            return ValueTask.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate)
                _observations.Clear();
        }

        public ResumptionDrainObservation[] Snapshot()
        {
            lock (_gate)
                return _observations.ToArray();
        }
    }

    private sealed record AuthorityObservation(
        string CheckpointName,
        string? ExecutableNodeId,
        string? SchedulerWorkItemId,
        AuthoritySnapshot? Authority);

    private sealed record ScheduleActivityDispatchObservation(
        string WorkItemId,
        string? ExecutableNodeId,
        string? PendingConsumeWorkItemId);

    private sealed record ResumptionDrainObservation(
        WorkflowExecutionCommandEnvelope Envelope,
        RuntimeSchedulerDrainResult Result);

    private sealed record AuthoritySnapshot(
        int Version,
        string Kind,
        string WorkflowExecutionId,
        string WorkItemId,
        string Fingerprint);

    private sealed class ArmedPostCommitBoundaryCrash
    {
        private int _armed = 1;

        public bool IsArmed => Volatile.Read(ref _armed) == 1;

        public void Disarm() => Interlocked.Exchange(ref _armed, 0);
    }

    private sealed class PhysicalFoldObserver
    {
        private int _maximumMemberCount;

        public int MaximumMemberCount => Volatile.Read(ref _maximumMemberCount);

        public void Record(RuntimeCheckpointPreparedFoldRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var current = MaximumMemberCount;
            while (request.Members.Count > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximumMemberCount, request.Members.Count, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class PostCommitBoundaryCrashStore(
        IRuntimeCheckpointCommitStore inner,
        ArmedPostCommitBoundaryCrash boundaryCrash,
        PhysicalFoldObserver physicalFolds) :
        IRuntimeCheckpointCommitStore,
        IRuntimeCheckpointPreparedLedgerStore
    {
        private IRuntimeCheckpointPreparedLedgerStore PreparedLedger =>
            inner as IRuntimeCheckpointPreparedLedgerStore
            ?? throw new InvalidOperationException("The boundary wrapper requires the durable prepared-ledger capability.");

        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
            RuntimeCheckpointPrepareRequest request,
            CancellationToken cancellationToken = default) =>
            inner.PrepareAsync(request, cancellationToken);

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CommitPreparedAsync(token, commit, decision, cancellationToken);
            if (boundaryCrash.IsArmed &&
                decision.Mode == RuntimeCheckpointPersistenceMode.Immediate &&
                commit.StateChanges.PostCommitOutbox.Count > 0)
            {
                throw new OperationCanceledException(
                    $"Stopped after honest post-commit boundary '{commit.CommitId}' to stage its durable continuation.");
            }

            return result;
        }

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
            RuntimeCheckpointPreparedQuery query,
            CancellationToken cancellationToken = default) =>
            PreparedLedger.PagePreparedAsync(query, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedAdoptionReceipt> AdoptPreparedAsync(
            RuntimeCheckpointPreparedAdoptionRequest request,
            CancellationToken cancellationToken = default) =>
            PreparedLedger.AdoptPreparedAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
            RuntimeCheckpointPreparedFoldRequest request,
            CancellationToken cancellationToken = default)
        {
            physicalFolds.Record(request);
            return PreparedLedger.CommitPreparedFoldAsync(request, cancellationToken);
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, decision, cancellationToken);
    }

}
