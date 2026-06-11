using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeApiFeatureTests
{
    [Fact]
    public void RegistersRuntimeExecutionServicesAndRequestHandlers()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowExecutableStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkQueue) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowSchedulerWorkQueue));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowExecutionStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActivityExecutionStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryActivityExecutionStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBookmarkStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryBookmarkStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDurableValueStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryDurableValueStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IIncidentStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryIncidentStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOperationalStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryOperationalStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IControlPlaneStateStore) &&
            descriptor.ImplementationType == typeof(InMemoryControlPlaneStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimePauseDecisionProvider) &&
            descriptor.ImplementationType == typeof(RuntimePauseDecisionProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeRecoveryScanner) &&
            descriptor.ImplementationType == typeof(InMemoryRuntimeRecoveryScanner));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeDomainRetryPolicy) &&
            descriptor.ImplementationType == typeof(NoopRuntimeDomainRetryPolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeVolatileWaitPolicy) &&
            descriptor.ImplementationType == typeof(DefaultRuntimeVolatileWaitPolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeGeneratorEmissionScheduler) &&
            descriptor.ImplementationType == typeof(RuntimeGeneratorEmissionScheduler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISchedulerStateStore) &&
            descriptor.ImplementationType == typeof(InMemorySchedulerStateStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimePostCommitOutboxStore) &&
            descriptor.ImplementationType == typeof(InMemoryRuntimePostCommitOutboxStore));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimePostCommitOutboxProcessor) &&
            descriptor.ImplementationType == typeof(RuntimePostCommitOutboxProcessor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionCommandProcessor) &&
            descriptor.ImplementationType == typeof(WorkflowSchedulerCommandProcessor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainer) &&
            descriptor.ImplementationType == typeof(WorkflowSchedulerDrainer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainPolicy) &&
            descriptor.ImplementationType == typeof(ImmediateWorkflowSchedulerDrainPolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeCheckpointPersistencePolicy) &&
            descriptor.ImplementationType == typeof(ImmediateRuntimeCheckpointPersistencePolicy));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeCheckpointWriter) &&
            descriptor.ImplementationType == typeof(InMemoryRuntimeCheckpointWriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimePostCommitIntentDispatcher) &&
            descriptor.ImplementationType == typeof(RuntimeSchedulerPostCommitIntentDispatcher));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(RuntimeCheckpointCommitter) &&
            descriptor.ImplementationType == typeof(RuntimeCheckpointCommitter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer) &&
            descriptor.ImplementationType == typeof(RuntimeActivityInputMaterializer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerDrainObserver) &&
            descriptor.ImplementationType == typeof(NoopWorkflowSchedulerDrainObserver));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowStartSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowScheduleActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowStartActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowCompleteActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowCheckpointSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(MissingActivityInvocationSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(MissingGeneratedEventSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(NoopWorkflowSchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionAgentProvider) &&
            descriptor.ImplementationType == typeof(InProcessWorkflowExecutionAgentProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeExecutionIdGenerator) &&
            descriptor.ImplementationType == typeof(GuidRuntimeExecutionIdGenerator) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutionStartDispatcher) &&
            descriptor.ImplementationType == typeof(WorkflowExecutionStartDispatcher) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InProcessWorkflowExecutionAgentProvider>(provider.GetRequiredService<IWorkflowExecutionAgentProvider>());
        Assert.IsType<WorkflowSchedulerCommandProcessor>(provider.GetRequiredService<IWorkflowExecutionCommandProcessor>());
        Assert.IsType<InMemoryWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<InMemoryWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<InMemoryActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
        Assert.IsType<InMemoryDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<InMemoryIncidentStateStore>(provider.GetRequiredService<IIncidentStateStore>());
        Assert.IsType<InMemoryOperationalStateStore>(provider.GetRequiredService<IOperationalStateStore>());
        Assert.IsType<InMemoryControlPlaneStateStore>(provider.GetRequiredService<IControlPlaneStateStore>());
        Assert.IsType<RuntimePauseDecisionProvider>(provider.GetRequiredService<IRuntimePauseDecisionProvider>());
        Assert.IsType<InMemoryRuntimeRecoveryScanner>(provider.GetRequiredService<IRuntimeRecoveryScanner>());
        Assert.IsType<NoopRuntimeDomainRetryPolicy>(provider.GetRequiredService<IRuntimeDomainRetryPolicy>());
        Assert.IsType<DefaultRuntimeVolatileWaitPolicy>(provider.GetRequiredService<IRuntimeVolatileWaitPolicy>());
        Assert.IsType<RuntimeGeneratorEmissionScheduler>(provider.GetRequiredService<IRuntimeGeneratorEmissionScheduler>());
        Assert.IsType<InMemorySchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.IsType<InMemoryRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<RuntimePostCommitOutboxProcessor>(provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>());
        Assert.IsType<WorkflowSchedulerDrainer>(provider.GetRequiredService<IWorkflowSchedulerDrainer>());
        Assert.IsType<ImmediateWorkflowSchedulerDrainPolicy>(provider.GetRequiredService<IWorkflowSchedulerDrainPolicy>());
        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<InMemoryRuntimeCheckpointWriter>(provider.GetRequiredService<IRuntimeCheckpointWriter>());
        Assert.IsType<RuntimeSchedulerPostCommitIntentDispatcher>(provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>());
        Assert.IsType<RuntimeCheckpointCommitter>(provider.GetRequiredService<RuntimeCheckpointCommitter>());
        Assert.IsType<RuntimeActivityInputMaterializer>(provider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.IsType<GuidRuntimeExecutionIdGenerator>(provider.GetRequiredService<IRuntimeExecutionIdGenerator>());
        Assert.IsType<WorkflowExecutionStartDispatcher>(provider.GetRequiredService<IWorkflowExecutionStartDispatcher>());
        Assert.Contains(provider.GetServices<IWorkflowSchedulerDrainObserver>(), observer => observer is NoopWorkflowSchedulerDrainObserver);
        var schedulerWorkHandlers = provider.GetServices<IWorkflowSchedulerWorkHandler>().ToArray();
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingGeneratedEventSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is NoopWorkflowSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is IFallbackWorkflowSchedulerWorkHandler);
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingGeneratedEventSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingGeneratedEventSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is NoopWorkflowSchedulerWorkHandler));
    }

    [Fact]
    public void RegistersRuntimeDomainRetryPolicyAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeDomainRetryPolicy>(new CustomRuntimeDomainRetryPolicy());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimeDomainRetryPolicy>(provider.GetRequiredService<IRuntimeDomainRetryPolicy>());
    }

    [Fact]
    public void RegistersRuntimeVolatileWaitPolicyAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeVolatileWaitPolicy>(new CustomRuntimeVolatileWaitPolicy());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimeVolatileWaitPolicy>(provider.GetRequiredService<IRuntimeVolatileWaitPolicy>());
    }

    [Fact]
    public void RegistersRuntimePauseDecisionProviderAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimePauseDecisionProvider>(new CustomRuntimePauseDecisionProvider());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimePauseDecisionProvider>(provider.GetRequiredService<IRuntimePauseDecisionProvider>());
    }

    [Fact]
    public void RegistersRuntimeGeneratorEmissionSchedulerAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeGeneratorEmissionScheduler>(new CustomRuntimeGeneratorEmissionScheduler());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimeGeneratorEmissionScheduler>(provider.GetRequiredService<IRuntimeGeneratorEmissionScheduler>());
    }

    [Fact]
    public void RegistersControlPlaneStateStoreAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IControlPlaneStateStore>(new CustomControlPlaneStateStore());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomControlPlaneStateStore>(provider.GetRequiredService<IControlPlaneStateStore>());
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsBookmarksIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointWriter>();
        var bookmarkStateStore = provider.GetRequiredService<IBookmarkStateStore>();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: RuntimeCheckpointNames.BookmarkCreated,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks:
                [
                    new RuntimeStateChange<BookmarkState>(
                        StateId: "bookmark-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: new BookmarkState(
                            BookmarkId: "bookmark-1",
                            WorkflowExecutionId: "wfexec-1",
                            ActivityExecutionId: "actexec-1",
                            ExecutableNodeId: "node-1",
                            ResumeTargetId: "node-resume-1",
                            StimulusType: "delivery-status",
                            StimulusHash: "sha256:delivery-status:order-123",
                            Payload: null,
                            Metadata: new Dictionary<string, string>(),
                            CreatedAt: now,
                            ExpiresAt: null),
                        Metadata: new Dictionary<string, string>())
                ],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsSchedulerStateIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 14, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointWriter>();
        var schedulerStateStore = provider.GetRequiredService<ISchedulerStateStore>();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: RuntimeCheckpointNames.ActivityScheduled,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: new RuntimeStateChange<SchedulerState>(
                    StateId: "wfexec-1",
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: new SchedulerState(
                        workflowExecutionId: "wfexec-1",
                        version: 2,
                        pendingWork:
                        [
                            new ScheduledActivityWorkItem(
                                WorkItemId: "work-1",
                                WorkflowExecutionId: "wfexec-1",
                                ExecutableNodeId: "node-1",
                                ActivityExecutionId: null,
                                SchedulingActivityExecutionId: "actexec-1",
                                BranchId: "branch-1",
                                IterationId: null,
                                EnqueuedAt: now,
                                Reason: "test")
                        ],
                        pendingContinuations: [],
                        volatileWaits: []),
                    Metadata: new Dictionary<string, string>()),
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var schedulerState = await schedulerStateStore.FindAsync("wfexec-1");
        Assert.NotNull(schedulerState);
        Assert.Equal(2, schedulerState.Version);
        Assert.Equal("node-1", Assert.Single(schedulerState.PendingWork).ExecutableNodeId);
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsDurableValuesIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 11, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointWriter>();
        var durableValueStateStore = provider.GetRequiredService<IDurableValueStateStore>();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: RuntimeCheckpointNames.DurableValueCaptured,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues:
                [
                    new RuntimeStateChange<DurableValueState>(
                        StateId: "durable-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: new DurableValueState(
                            durableValueId: "durable-1",
                            workflowExecutionId: "wfexec-1",
                            valueId: "customer",
                            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
                            lifecycle: DurableValueLifecycle.Instance,
                            storage: DurableValueStorage.Inline,
                            inlineValue: Json("""{"id":"customer-1"}"""),
                            externalReference: null,
                            sourceActivityExecutionId: "actexec-1",
                            capturedAt: now,
                            metadata: new Dictionary<string, string>()),
                        Metadata: new Dictionary<string, string>())
                ],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await durableValueStateStore.FindAsync("wfexec-1", "durable-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsIncidentsIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointWriter>();
        var incidentStateStore = provider.GetRequiredService<IIncidentStateStore>();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: RuntimeCheckpointNames.IncidentRecorded,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        StateId: "incident-1",
                        Operation: RuntimeStateChangeOperation.Append,
                        State: new IncidentState(
                            incidentId: "incident-1",
                            workflowExecutionId: "wfexec-1",
                            activityExecutionId: "actexec-1",
                            executableNodeId: "node-1",
                            severity: IncidentSeverity.Error,
                            status: IncidentStatus.Blocking,
                            resolutionAction: IncidentResolutionAction.FaultWorkflow,
                            failureType: "ActivityFaulted",
                            message: "Activity failed.",
                            createdAt: now,
                            resolvedAt: null,
                            metadata: new Dictionary<string, string>()),
                        Metadata: new Dictionary<string, string>())
                ],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await incidentStateStore.FindAsync("wfexec-1", "incident-1"));
        Assert.Single(await incidentStateStore.ListBlockingAsync("wfexec-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsOperationalStateIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointWriter>();
        var operationalStateStore = provider.GetRequiredService<IOperationalStateStore>();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: RuntimeCheckpointNames.PostCommitIntentRecorded,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational:
                [
                    new RuntimeStateChange<OperationalState>(
                        StateId: "operational-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: new OperationalState(
                            operationalStateId: "operational-1",
                            workflowExecutionId: "wfexec-1",
                            executionLease: new RuntimeExecutionLease(
                                leaseId: "lease-1",
                                workflowExecutionId: "wfexec-1",
                                ownerId: "worker-1",
                                acquiredAt: now,
                                expiresAt: now.AddMinutes(5),
                                fencingToken: 1),
                            heartbeat: new RuntimeHeartbeat(
                                heartbeatId: "heartbeat-1",
                                workflowExecutionId: "wfexec-1",
                                ownerId: "worker-1",
                                leaseId: "lease-1",
                                recordedAt: now),
                            drain: null,
                            interruptedExecution: null,
                            pendingPostCommitIntentIds: ["intent-1"],
                            metadata: new Dictionary<string, string>()),
                        Metadata: new Dictionary<string, string>())
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var operationalState = await operationalStateStore.FindAsync("wfexec-1", "operational-1");
        Assert.NotNull(operationalState);
        Assert.Equal("worker-1", operationalState.ExecutionLease!.OwnerId);
    }

    private static System.Text.Json.JsonElement Json(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class CustomRuntimeDomainRetryPolicy : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) =>
            new(
                mode: RuntimeDomainRetryMode.Fault,
                delay: null,
                reason: "Custom policy owns domain retry decisions.");
    }

    private sealed class CustomRuntimeVolatileWaitPolicy : IRuntimeVolatileWaitPolicy
    {
        public RuntimeVolatileWaitPolicyDecision Decide(RuntimeVolatileWaitPolicyRequest request) =>
            new(
                isAllowed: false,
                hostShutdownBehavior: request.RequestedHostShutdownBehavior,
                cancellationBehavior: request.RequestedCancellationBehavior,
                durableFallbackPolicy: request.DurableFallbackPolicy,
                maximumDuration: request.RequestedDuration,
                reason: "Custom policy owns volatile wait decisions.");
    }

    private sealed class CustomRuntimePauseDecisionProvider : IRuntimePauseDecisionProvider
    {
        public ValueTask<SchedulerPauseDecision> DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default) =>
            new(new SchedulerPauseDecision(
                canAdvance: true,
                boundary: request.Boundary,
                continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
                holdId: null,
                reason: null));
    }

    private sealed class CustomRuntimeGeneratorEmissionScheduler : IRuntimeGeneratorEmissionScheduler
    {
        public ValueTask<RuntimeGeneratorEmissionScheduleResult> ScheduleAsync(
            RuntimeGeneratorEmissionScheduleRequest request,
            CancellationToken cancellationToken = default)
        {
            var generatedEventWorkItem = new SchedulerGeneratedEventWorkItem(
                workItemId: "custom-generated-work",
                generatedEvent: request.GeneratedEvent,
                enqueuedAt: request.EnqueuedAt ?? DateTimeOffset.UnixEpoch,
                reason: request.Reason);
            var schedulerWorkItem = new RuntimeSchedulerWorkItem(
                workItemId: generatedEventWorkItem.WorkItemId,
                workflowExecutionId: request.GeneratedEvent.WorkflowExecutionId,
                commandId: "custom-command",
                commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
                envelopeId: "custom-envelope",
                idempotencyKey: "custom-idempotency",
                enqueuedAt: generatedEventWorkItem.EnqueuedAt,
                recordedAt: generatedEventWorkItem.EnqueuedAt);

            return new(new RuntimeGeneratorEmissionScheduleResult(generatedEventWorkItem, schedulerWorkItem));
        }
    }

    private sealed class CustomControlPlaneStateStore : IControlPlaneStateStore
    {
        public ValueTask<ControlPlaneState> SaveAsync(ControlPlaneState state, CancellationToken cancellationToken = default) => new(state);

        public ValueTask<ControlPlaneState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default) => new((ControlPlaneState?)null);

        public ValueTask<IReadOnlyCollection<ControlPlaneState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new(Array.Empty<ControlPlaneState>());

        public ValueTask<IReadOnlyCollection<ControlPlaneState>> ListAllAsync(CancellationToken cancellationToken = default) =>
            new(Array.Empty<ControlPlaneState>());
    }
}
