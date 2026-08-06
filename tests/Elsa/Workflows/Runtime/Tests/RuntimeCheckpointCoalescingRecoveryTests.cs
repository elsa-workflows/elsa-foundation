using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// T026 RED. These facts construct the durable D1 source and its three real Prepared reservations before touching the
/// T027-only recovery boundary. They are intentionally not API-shape assertions: a recovery coordinator has to operate
/// on this durable queue/ledger state without inventing a second source or changing immutable preparation identity.
/// </summary>
public sealed class RuntimeCheckpointCoalescingRecoveryTests
{
    [Fact]
    public async Task D1_schedule_start_invoke_reservations_retain_exact_identity_and_normal_source_redelivery()
    {
        var fixture = await DurableD1Fixture.CreateAsync();
        var before = fixture.Snapshot();

        Assert.Equal(3, before.Prepared.Count);
        Assert.Equal(["ActivityScheduled", "ActivityStarted", "ActivityAttemptClaimed"], before.Prepared.Select(item => item.CheckpointName));
        Assert.Equal([1L, 2L, 3L], before.Prepared.Select(item => item.WorkflowCheckpointOrder));
        Assert.Equal(fixture.Authorities, before.Prepared.Select(reservation => reservation.RecoveryAuthority));
        Assert.All(before.Prepared, reservation => Assert.NotNull(reservation.CanonicalInputPayload));
        Assert.All(before.Prepared, reservation => Assert.NotEmpty(reservation.ProvenanceBytes));
        await fixture.RedriveOriginalSourceAsync();
        AssertPreparedEvidenceUnchanged(before, fixture.Snapshot());
        Assert.Equal(
            new[] { fixture.Start.WorkItemId, fixture.Invoke.WorkItemId },
            (await fixture.ListWorkItemsAsync()).Select(item => item.WorkItemId));

        // A normal redelivery above exercised the real ScheduleActivity handler against its original queued source.
        // T027 must retain that identity/status convergence and may not synthesize Schedule/Start/Invoke work.
        var recovery = RequireT027RecoveryOperation();
        await InvokeRecoveryAsync(recovery, fixture);

        var after = fixture.Snapshot();
        AssertPreparedEvidenceUnchanged(before, after);
        Assert.Equal(
            new[] { fixture.Start.WorkItemId, fixture.Invoke.WorkItemId },
            (await fixture.ListWorkItemsAsync()).Select(item => item.WorkItemId));
    }

    [Fact]
    public async Task Source_bound_exact_recovery_never_replays_or_folds_and_leaves_prepared_for_the_original_source()
    {
        var fixture = await DurableD1Fixture.CreateAsync();
        var before = fixture.Snapshot();
        var replayer = new CountingReplayer();
        var foldObserver = new CountingFoldObserver();

        var recovery = RequireT027RecoveryOperation();
        await InvokeRecoveryAsync(recovery, fixture, replayer, foldObserver);

        Assert.Equal(0, replayer.CallCount);
        Assert.Equal(0, foldObserver.CallCount);
        Assert.Equal(before, fixture.Snapshot());
        Assert.All(fixture.Store.ListLogicalCheckpointLedgerEntries(), entry =>
        {
            Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, entry.Status);
            Assert.Contains(entry.RecoveryAuthority, fixture.Authorities);
        });
    }

    [Fact]
    public async Task Only_an_exact_contiguous_originally_source_free_prefix_can_rehydrate_and_fold()
    {
        var fixture = await SourceFreePrefixFixture.CreateAsync();
        var before = fixture.Snapshot();
        var replayer = new CountingReplayer();
        var foldObserver = new CountingFoldObserver();

        var recovery = RequireT027RecoveryOperation();
        await InvokeRecoveryAsync(recovery, fixture, replayer, foldObserver);

        Assert.Equal(2, replayer.CallCount);
        Assert.Equal(1, foldObserver.CallCount);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, fixture.Store.ListLogicalCheckpointLedgerEntries().Single(entry => entry.CommitId == "source-bound-gap").Status);
        Assert.NotEqual(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData("hidden-source-bound-gap")]
    [InlineData("noncontiguous-source-free-prefix")]
    [InlineData("mixed-source")]
    [InlineData("missing-source")]
    [InlineData("unsupported-source")]
    [InlineData("ambiguous-source")]
    public async Task Unsafe_recovery_candidates_do_not_mutate_or_call_replayer_or_fold(string shape)
    {
        var fixture = await SourceFreePrefixFixture.CreateAsync(shape);
        var before = fixture.Snapshot();
        var replayer = new CountingReplayer();
        var foldObserver = new CountingFoldObserver();

        var recovery = RequireT027RecoveryOperation();
        await InvokeRecoveryAsync(recovery, fixture, replayer, foldObserver);

        Assert.Equal(0, replayer.CallCount);
        Assert.Equal(0, foldObserver.CallCount);
        Assert.Equal(before, fixture.Snapshot());
    }

    // Compile-safe only at the absent T027 boundary. The fixture itself is fully typed and durable; this reflection
    // makes the first RED failure precisely the coordinator/operation that T027 owns rather than allowing partial
    // pre-T027 implementation to determine the test shape.
    private static MethodInfo RequireT027RecoveryOperation()
    {
        var coordinator = typeof(WorkflowDrainOrchestrator).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Services.Coalescing.CoalescingRuntimeCheckpointRecoveryCoordinator");
        Assert.NotNull(coordinator);
        var operation = coordinator!.GetMethod("RecoverPreparedAsync", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(operation);
        return operation!;
    }

    private static async Task InvokeRecoveryAsync(
        MethodInfo operation,
        DurableRecoveryFixture fixture,
        CountingReplayer? replayer = null,
        CountingFoldObserver? foldObserver = null)
    {
        // T027 supplies one coordinator constructor that takes the explicit ledger, source resolver/router, shared
        // replayer, and fold capability. The named argument map rejects a service-locator/synthetic-source shortcut.
        var instance = CreateCoordinator(operation.DeclaringType!, fixture, replayer, foldObserver);
        var result = operation.Invoke(instance, [fixture.WorkflowExecutionId, CancellationToken.None]);
        Assert.NotNull(result);
        var task = (Task)result!.GetType().GetMethod("AsTask")!.Invoke(result, [])!;
        await task;
    }

    private static object CreateCoordinator(
        Type coordinatorType,
        DurableRecoveryFixture fixture,
        CountingReplayer? replayer,
        CountingFoldObserver? foldObserver)
    {
        var constructor = coordinatorType.GetConstructors().SingleOrDefault(candidate => candidate.GetParameters().Length > 0);
        Assert.NotNull(constructor);
        var arguments = constructor!.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(IRuntimeCheckpointPreparationReplayer))
                return replayer ?? new CountingReplayer();
            if (parameter.ParameterType == typeof(IRuntimeCheckpointPreparedLedgerStore))
                return new ObservingPreparedLedgerStore(fixture.Store, foldObserver);
            if (parameter.ParameterType == typeof(IRuntimeSchedulerWorkItemResolver))
                return fixture.Resolver;
            if (parameter.ParameterType == typeof(IRuntimeCheckpointCommitStore))
                return fixture.Store;
            if (parameter.ParameterType.IsInstanceOfType(fixture.Queue))
                return fixture.Queue;
            if (parameter.ParameterType.IsInstanceOfType(fixture.Store))
                return (object)fixture.Store;
            Assert.Fail($"T027 recovery coordinator constructor has unsupported dependency '{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        return constructor.Invoke(arguments!);
    }

    private abstract class DurableRecoveryFixture
    {
        protected DurableRecoveryFixture(
            InMemoryRuntimeCheckpointCommitStore store,
            IWorkflowSchedulerWorkQueue queue,
            IRuntimeSchedulerWorkItemResolver resolver,
            string workflowExecutionId)
        {
            Store = store;
            Queue = queue;
            Resolver = resolver;
            WorkflowExecutionId = workflowExecutionId;
        }

        public InMemoryRuntimeCheckpointCommitStore Store { get; }
        public IWorkflowSchedulerWorkQueue Queue { get; }
        public IRuntimeSchedulerWorkItemResolver Resolver { get; }
        public string WorkflowExecutionId { get; }

        public DurableSnapshot Snapshot() => new(
            Store.ListLogicalCheckpointLedgerEntries()
                .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
                .Select(entry => new PreparedSnapshot(
                    entry.CommitId,
                    entry.LedgerToken,
                    entry.RawCheckpoint!.Name,
                    entry.Provenance.WorkflowCheckpointOrder,
                    entry.RecoveryAuthority,
                    entry.InputFingerprint,
                    entry.CanonicalInputPayload,
                    JsonSerializer.Serialize(entry.Provenance),
                    entry.Status))
                .ToArray(),
            JsonSerializer.Serialize(Store.ListCommits()),
            Store.GetCheckpointOrderHighWatermarks(WorkflowExecutionId));
    }

    private sealed class DurableD1Fixture : DurableRecoveryFixture
    {
        private DurableD1Fixture(
            InMemoryRuntimeCheckpointCommitStore store,
            IWorkflowSchedulerWorkQueue queue,
            RuntimeSchedulerWorkItem source,
            RuntimeSchedulerWorkItem start,
            RuntimeSchedulerWorkItem invoke,
            IReadOnlyList<RuntimeCheckpointRecoveryAuthority> authorities,
            Func<Task> redriveOriginalSourceAsync,
            Func<Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>>> listWorkItemsAsync)
            : base(store, queue, new DurableSchedulerWorkResolver(queue), source.WorkflowExecutionId)
        {
            Source = source;
            Start = start;
            Invoke = invoke;
            Authorities = authorities;
            RedriveOriginalSourceAsync = redriveOriginalSourceAsync;
            ListWorkItemsAsync = listWorkItemsAsync;
        }

        public RuntimeSchedulerWorkItem Source { get; }
        public RuntimeSchedulerWorkItem Start { get; }
        public RuntimeSchedulerWorkItem Invoke { get; }
        public IReadOnlyList<RuntimeCheckpointRecoveryAuthority> Authorities { get; }
        public Func<Task> RedriveOriginalSourceAsync { get; }
        public Func<Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>>> ListWorkItemsAsync { get; }

        public static async Task<DurableD1Fixture> CreateAsync()
        {
            const string workflowExecutionId = "workflow-d1";
            var chain = await ActualD1SchedulerChain.CreateAsync(workflowExecutionId);
            return new(
                chain.Store,
                chain.Queue,
                chain.Source,
                chain.Start,
                chain.Invoke,
                chain.Authorities,
                chain.RedriveOriginalSourceAsync,
                chain.ListWorkItemsAsync);
        }
    }

    private sealed class SourceFreePrefixFixture : DurableRecoveryFixture
    {
        private SourceFreePrefixFixture(
            InMemoryRuntimeCheckpointCommitStore store,
            IWorkflowSchedulerWorkQueue queue,
            IRuntimeSchedulerWorkItemResolver resolver,
            string workflowExecutionId)
            : base(store, queue, resolver, workflowExecutionId)
        {
        }

        public static async Task<SourceFreePrefixFixture> CreateAsync(string? shape = null)
        {
            const string workflowExecutionId = "workflow-source-free";
            var store = new InMemoryRuntimeCheckpointCommitStore();
            var queue = new InMemoryWorkflowSchedulerWorkQueue();
            var source = NewSource(workflowExecutionId, "source-primary");
            var alternateSource = NewSource(workflowExecutionId, "source-alternate");
            await queue.EnqueueAsync(source);
            await queue.EnqueueAsync(alternateSource);
            var codec = new RuntimeCheckpointRecoveryAuthorityCodec();
            var authority = codec.Encode(source);
            var alternateAuthority = codec.Encode(alternateSource);
            var resolver = new ScriptedSchedulerWorkResolver(queue);

            switch (shape)
            {
                case null:
                    // The only admissible T026 recovery shape: a contiguous originally-source-free prefix followed
                    // by a real source-bound member that must remain untouched.
                    await PrepareAsync(store, workflowExecutionId, "source-free-1", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "source-free-2", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "source-bound-gap", authority);
                    resolver.SetExact(authority, source);
                    break;
                case "hidden-source-bound-gap":
                    await PrepareAsync(store, workflowExecutionId, "source-free-before-gap", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "hidden-source-bound-gap", authority);
                    await PrepareAsync(store, workflowExecutionId, "source-free-after-gap", authority: null);
                    resolver.SetExact(authority, source);
                    break;
                case "noncontiguous-source-free-prefix":
                    await PrepareAsync(store, workflowExecutionId, "source-bound-before-prefix", authority);
                    await PrepareAsync(store, workflowExecutionId, "source-free-after-source", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "source-free-late", authority: null);
                    resolver.SetExact(authority, source);
                    break;
                case "mixed-source":
                    await PrepareAsync(store, workflowExecutionId, "source-free-before-mixed", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "first-source-bound", authority);
                    await PrepareAsync(store, workflowExecutionId, "second-source-bound", alternateAuthority);
                    resolver.SetExact(authority, source);
                    resolver.SetExact(alternateAuthority, alternateSource);
                    break;
                case "missing-source":
                    await PrepareAsync(store, workflowExecutionId, "source-free-before-missing", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "missing-source", authority);
                    resolver.Set(authority, RuntimeCheckpointRecoveryStatus.Missing);
                    break;
                case "unsupported-source":
                    await PrepareAsync(store, workflowExecutionId, "source-free-before-unsupported", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "unsupported-source", authority);
                    resolver.Set(authority, RuntimeCheckpointRecoveryStatus.UnsupportedVersionOrKind);
                    break;
                case "ambiguous-source":
                    await PrepareAsync(store, workflowExecutionId, "source-free-before-ambiguous", authority: null);
                    await PrepareAsync(store, workflowExecutionId, "ambiguous-source", authority);
                    resolver.Set(authority, RuntimeCheckpointRecoveryStatus.Ambiguous);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown T026 recovery fixture shape.");
            }

            return new(store, queue, resolver, workflowExecutionId);
        }
    }

    /// <summary>
    /// Real discrete D1 handler chain. The first two continuation commands are delivered through the normal overlay
    /// outbox processor; the real invoke handler is stopped immediately after its durable attempt-claim boundary, so
    /// this T026 prefix contains precisely the three source-command preparations and no terminal completion checkpoint.
    /// </summary>
    private sealed class ActualD1SchedulerChain
    {
        private ActualD1SchedulerChain(
            InMemoryRuntimeCheckpointCommitStore store,
            IWorkflowSchedulerWorkQueue queue,
            RuntimeSchedulerWorkItem source,
            RuntimeSchedulerWorkItem start,
            RuntimeSchedulerWorkItem invoke,
            IReadOnlyList<RuntimeCheckpointRecoveryAuthority> authorities,
            Func<Task> redriveOriginalSourceAsync,
            Func<Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>>> listWorkItemsAsync)
        {
            Store = store;
            Queue = queue;
            Source = source;
            Start = start;
            Invoke = invoke;
            Authorities = authorities;
            RedriveOriginalSourceAsync = redriveOriginalSourceAsync;
            ListWorkItemsAsync = listWorkItemsAsync;
        }

        public InMemoryRuntimeCheckpointCommitStore Store { get; }
        public IWorkflowSchedulerWorkQueue Queue { get; }
        public RuntimeSchedulerWorkItem Source { get; }
        public RuntimeSchedulerWorkItem Start { get; }
        public RuntimeSchedulerWorkItem Invoke { get; }
        public IReadOnlyList<RuntimeCheckpointRecoveryAuthority> Authorities { get; }
        public Func<Task> RedriveOriginalSourceAsync { get; }
        public Func<Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>>> ListWorkItemsAsync { get; }

        public static async Task<ActualD1SchedulerChain> CreateAsync(string workflowExecutionId)
        {
            var now = DateTimeOffset.UnixEpoch;
            var executable = NewExecutable();
            var executableStore = new InMemoryWorkflowExecutableStore();
            await executableStore.SaveAsync(executable);
            var innerActivityStore = new InMemoryActivityExecutionStateStore();
            var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
            var durableValueStore = new InMemoryDurableValueStateStore();
            var incidentStore = new InMemoryIncidentStateStore();
            var inspectionStore = new InMemoryActivityExecutionInspectionStore();
            var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
            var rootFrame = new VariableFrameFactory().CreateRoot(
                workflowExecutionId,
                VariableReference.WorkflowScopeId,
                new Dictionary<string, ValueEnvelope>());
            await workflowStateStore.SaveAsync(new WorkflowExecutionState(
                workflowExecutionId,
                executable.Identity,
                WorkflowExecutionStatus.Running,
                null,
                now,
                now,
                now,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>())
            {
                RootVariableFrame = rootFrame
            });
            var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
                workflowStateStore,
                innerActivityStore,
                bookmarkStateStore: null,
                durableValueStore,
                incidentStore,
                operationalStateStore: null,
                schedulerStateStore: null,
                inspectionStore);
            var sessionAccessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            var session = new RuntimeCoalescingSession(
                workflowExecutionId,
                innerQueue,
                new CoalescingRuntimeCheckpointPersistenceOptions(),
                checkpointStore);
            var queue = new CoalescingWorkflowSchedulerWorkQueue(
                new CoalescingInner<IWorkflowSchedulerWorkQueue>(innerQueue), sessionAccessor);
            var activityStore = new CoalescingActivityExecutionStateStore(
                new CoalescingInner<IActivityExecutionStateStore>(innerActivityStore), sessionAccessor);
            var coalescedValueStore = new CoalescingDurableValueStateStore(
                new CoalescingInner<IDurableValueStateStore>(durableValueStore), sessionAccessor);
            var coalescedCheckpointStore = new CoalescingRuntimeCheckpointCommitStore(
                new CoalescingInner<IRuntimeCheckpointCommitStore>(checkpointStore), sessionAccessor);
            var coalescedOutboxStore = new CoalescingRuntimePostCommitOutboxStore(
                new CoalescingInner<IRuntimePostCommitOutboxStore>(checkpointStore), sessionAccessor);
            var accessor = new AmbientCheckpointRecoveryAuthorityAccessor();
            var committer = new RuntimeCheckpointCommitter(
                new CoalescingRuntimeCheckpointPersistencePolicy(), coalescedCheckpointStore,
                ownershipContextAccessor: null, tracer: null, enrichers: [], intentHandlerContributions: [],
                recoveryAuthorityAccessor: accessor);
            var source = NewScheduleSource(workflowExecutionId, executable.Identity, now);
            await innerQueue.EnqueueAsync(source);
            var codec = new RuntimeCheckpointRecoveryAuthorityCodec();
            var sourceAuthority = codec.Encode(source);
            var inspectionAccumulator = new RuntimeActivityExecutionInspectionAccumulator(inspectionStore);
            var schedule = new WorkflowScheduleActivitySchedulerWorkHandler(
                executableStore, activityStore, queue, committer, inspectionAccumulator, new FixedTimeProvider(now));
            var drainer = new WorkflowSchedulerDrainer(
                queue,
                [schedule],
                workflowStateStore,
                new FixedTimeProvider(now),
                recoveryAuthorityAccessor: accessor);
            var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(queue);
            var outboxProcessor = new RuntimePostCommitOutboxProcessor(coalescedOutboxStore, dispatcher, new FixedTimeProvider(now));

            using (sessionAccessor.Push(session))
            using (accessor.Push(sourceAuthority))
                await schedule.HandleAsync(source);

            using (sessionAccessor.Push(session))
                await outboxProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, workflowExecutionId));
            RuntimeSchedulerWorkItem start;
            using (sessionAccessor.Push(session))
                start = Assert.Single((await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId)))
                    .Where(item => item.CommandKind == WorkflowExecutionCommandKind.StartActivity));
            var startAuthority = codec.Encode(start);

            await using var startProvider = new ServiceCollection()
                .AddScoped<IRuntimeActivityInputMaterializer>(_ => new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver()))
                .BuildServiceProvider();
            var startHandler = new WorkflowStartActivitySchedulerWorkHandler(
                executableStore, activityStore, queue, committer, inspectionAccumulator, new FixedTimeProvider(now),
                startProvider.GetRequiredService<IServiceScopeFactory>(), coalescedValueStore, workflowStateStore);
            using (sessionAccessor.Push(session))
            using (accessor.Push(startAuthority))
                await startHandler.HandleAsync(start);

            using (sessionAccessor.Push(session))
                await outboxProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, workflowExecutionId));
            RuntimeSchedulerWorkItem invoke;
            using (sessionAccessor.Push(session))
                invoke = Assert.Single((await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId)))
                    .Where(item => item.CommandKind == WorkflowExecutionCommandKind.InvokeActivity));
            var invokeAuthority = codec.Encode(invoke);

            using var cancellation = new CancellationTokenSource();
            await using var invokeProvider = CreateInvokeProvider(
                cancellation,
                executableStore,
                activityStore,
                queue,
                coalescedValueStore,
                incidentStore,
                workflowStateStore,
                coalescedCheckpointStore,
                committer,
                now);
            var invokeHandler = new WorkflowInvokeActivitySchedulerWorkHandler(
                invokeProvider.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(now));
            using (sessionAccessor.Push(session))
            using (accessor.Push(invokeAuthority))
                await Assert.ThrowsAsync<OperationCanceledException>(() => invokeHandler.HandleAsync(invoke, cancellation.Token).AsTask());

            async Task RedriveOriginalSourceAsync()
            {
                using (sessionAccessor.Push(session))
                {
                    var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(workflowExecutionId, maxWorkItems: 1));
                    var delivered = Assert.Single(result.Items);
                    Assert.Equal(source.WorkItemId, delivered.WorkItemId);
                    Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, delivered.Status);
                }
            }

            async Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListWorkItemsAsync()
            {
                using (sessionAccessor.Push(session))
                    return await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId));
            }

            return new(
                checkpointStore,
                queue,
                source,
                start,
                invoke,
                [sourceAuthority, startAuthority, invokeAuthority],
                RedriveOriginalSourceAsync,
                ListWorkItemsAsync);
        }

        private static ServiceProvider CreateInvokeProvider(
            CancellationTokenSource cancellation,
            IWorkflowExecutableStore executableStore,
            IActivityExecutionStateStore activityStore,
            IWorkflowSchedulerWorkQueue queue,
            IDurableValueStateStore durableValueStore,
            IIncidentStateStore incidentStore,
            IWorkflowExecutionStateStore workflowStateStore,
            IRuntimeCheckpointCommitStore checkpointStore,
            RuntimeCheckpointCommitter committer,
            DateTimeOffset now)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IActivityActivator>(new CancellingD1Activator(cancellation));
            services.AddSingleton(executableStore);
            services.AddSingleton(activityStore);
            services.AddSingleton(queue);
            services.AddSingleton(durableValueStore);
            services.AddSingleton(incidentStore);
            services.AddSingleton(workflowStateStore);
            services.AddSingleton(checkpointStore);
            services.AddSingleton(committer);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            services.AddSingleton(sp => new ActivityFaultIncidentRecorder(sp.GetRequiredService<TimeProvider>()));
            services.AddSingleton<ActivityCompletionProjector>();
            services.AddSingleton<IRuntimeDurableValueStorageDriver, JsonRuntimeDurableValueStorageDriver>();
            services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry, RuntimeDurableValueStorageDriverRegistry>();
            services.AddSingleton<RuntimeOutputCaptureProjector>();
            services.AddSingleton<ActivityInputHydrator>();
            services.AddSingleton<IRuntimeExecutionIdGenerator, ShortRuntimeExecutionIdGenerator>();
            return services.BuildServiceProvider();
        }

        private static RuntimeSchedulerWorkItem NewScheduleSource(string workflowExecutionId, WorkflowExecutableIdentity identity, DateTimeOffset now) => new(
            "d1-schedule-source", workflowExecutionId, "command-d1-schedule", WorkflowExecutionCommandKind.ScheduleActivity,
            "envelope-d1", $"{workflowExecutionId}:schedule", now, now, 1,
            JsonSerializer.SerializeToElement(new RuntimeScheduleActivityCommandPayload(
                identity, "node-d1", "activity-d1", RuntimeScheduleActivityCommandPayload.WorkflowStartReason)));

        private static WorkflowExecutable NewExecutable()
        {
            var descriptor = JsonSerializer.SerializeToElement(new { type = "d1" });
            var contract = new ActivityContract(
                "test/d1",
                "1.0.0",
                "test/d1",
                descriptor,
                [],
                new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
                ["Done"],
                new ActivityActivationRequirement("d1", "test/d1"),
                SideEffectProfile.ReplaySafe);
            var node = new ExecutableNode(
                "node-d1", "authored-d1", "test/d1", "1.0.0", "test/d1", descriptor,
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
            return new(new("artifact-d1", "definition-d1", "version-d1", "1.0.0", "sha256:d1"), node,
                new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        }

        private sealed class CancellingD1Activator(CancellationTokenSource cancellation) : IActivityActivator
        {
            public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
            {
                cancellation.Cancel();
                return ValueTask.FromResult(new ActivityActivationLease(new D1Activity()));
            }
        }

        private sealed class D1Activity : Activity
        {
            protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
            }
        }
    }

    private static async Task PrepareAsync(
        InMemoryRuntimeCheckpointCommitStore store,
        string workflowExecutionId,
        string name,
        RuntimeCheckpointRecoveryAuthority? authority,
        string? commitId = null)
    {
        var id = commitId ?? name;
        var commit = new RuntimeCheckpointCommit(
            id,
            new RuntimeCheckpoint($"checkpoint:{id}", name, workflowExecutionId, DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());
        var prepared = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit) with { RecoveryAuthority = authority });
        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, prepared.Status);
    }

    private static RuntimeSchedulerWorkItem NewSource(string workflowExecutionId, string workItemId = "d1-schedule-source")
    {
        using var payload = JsonDocument.Parse("""{"node":"root"}""");
        return new(
            workItemId,
            workflowExecutionId,
            $"command:{workItemId}",
            WorkflowExecutionCommandKind.ScheduleActivity,
            $"envelope:{workItemId}",
            $"{workflowExecutionId}:{workItemId}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: payload.RootElement.Clone());
    }

    private sealed record PreparedSnapshot(
        string CommitId,
        string LedgerToken,
        string CheckpointName,
        long WorkflowCheckpointOrder,
        RuntimeCheckpointRecoveryAuthority? RecoveryAuthority,
        string Fingerprint,
        string? CanonicalInputPayload,
        string ProvenanceBytes,
        RuntimeLogicalCheckpointLedgerStatus Status);

    private sealed record DurableSnapshot(
        IReadOnlyList<PreparedSnapshot> Prepared,
        string Commits,
        (long Reserved, long Committed, long Revision) HighWatermarks);

    private static void AssertPreparedEvidenceUnchanged(DurableSnapshot expected, DurableSnapshot actual)
    {
        Assert.Equal(expected.Prepared, actual.Prepared);
        Assert.Equal(expected.Commits, actual.Commits);
    }

    private sealed class CountingReplayer : IRuntimeCheckpointPreparationReplayer
    {
        private readonly RuntimeCheckpointPreparationReplayer _inner = new(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            [],
            []);

        public int CallCount { get; private set; }

        public async ValueTask<RuntimeCheckpointPreparedCommit> RehydrateAsync(RuntimeCheckpointPreparedReservation reservation, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await _inner.RehydrateAsync(reservation, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingFoldObserver
    {
        public int CallCount { get; private set; }
        public void Record(RuntimeCheckpointPreparedFoldRequest request) => CallCount++;
    }

    /// <summary>Delegates every provider-owned operation while making the exact T027 fold invocation observable.</summary>
    private sealed class ObservingPreparedLedgerStore(
        IRuntimeCheckpointPreparedLedgerStore inner,
        CountingFoldObserver? foldObserver) : IRuntimeCheckpointPreparedLedgerStore
    {
        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default) =>
            inner.PrepareAsync(request, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, decision, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(RuntimeCheckpointPreparationToken token, RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            inner.CommitPreparedAsync(token, commit, decision, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(RuntimeCheckpointPreparedQuery query, CancellationToken cancellationToken = default) =>
            inner.PagePreparedAsync(query, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(RuntimeCheckpointPreparedFoldRequest request, CancellationToken cancellationToken = default)
        {
            foldObserver?.Record(request);
            return inner.CommitPreparedFoldAsync(request, cancellationToken);
        }
    }

    private sealed class ScriptedSchedulerWorkResolver(IWorkflowSchedulerWorkQueue queue) : IRuntimeSchedulerWorkItemResolver
    {
        private readonly Dictionary<string, RuntimeCheckpointRecoveryResolution> _results = new(StringComparer.Ordinal);

        public void SetExact(RuntimeCheckpointRecoveryAuthority authority, RuntimeSchedulerWorkItem item) =>
            _results[authority.WorkItemId] = new(RuntimeCheckpointRecoveryStatus.Exact, authority, item);

        public void Set(RuntimeCheckpointRecoveryAuthority authority, RuntimeCheckpointRecoveryStatus status) =>
            _results[authority.WorkItemId] = new(status, authority);

        public async ValueTask<RuntimeCheckpointRecoveryResolution> ResolveAsync(RuntimeCheckpointRecoveryAuthority authority, CancellationToken cancellationToken = default)
        {
            if (_results.TryGetValue(authority.WorkItemId, out var result))
                return result;

            var page = await queue.ListAsync(new RuntimeSchedulerWorkQuery(authority.WorkflowExecutionId), cancellationToken);
            var item = page.Items.SingleOrDefault(candidate => candidate.WorkItemId == authority.WorkItemId);
            return item is null
                ? new(RuntimeCheckpointRecoveryStatus.Missing, authority)
                : new(RuntimeCheckpointRecoveryStatus.Exact, authority, item);
        }
    }

    private sealed class DurableSchedulerWorkResolver(IWorkflowSchedulerWorkQueue queue) : IRuntimeSchedulerWorkItemResolver
    {
        public async ValueTask<RuntimeCheckpointRecoveryResolution> ResolveAsync(RuntimeCheckpointRecoveryAuthority authority, CancellationToken cancellationToken = default)
        {
            var page = await queue.ListAsync(new(authority.WorkflowExecutionId), cancellationToken);
            var item = page.Items.SingleOrDefault(candidate => candidate.WorkItemId == authority.WorkItemId);
            return item is null
                ? new(RuntimeCheckpointRecoveryStatus.Missing)
                : new(RuntimeCheckpointRecoveryStatus.Exact, authority, item);
        }
    }
}
