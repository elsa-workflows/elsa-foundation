using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
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

        // TS-1 (constitution §2.23.1): assert registration presence + resolvability, not internal wiring.
        // ImplementationType / ServiceLifetime / ImplementationFactory pinning was removed so that swapping an
        // equivalent implementation no longer trips this test. Behavioural contracts are preserved: the
        // negative-wiring assertions prove certain contracts are deliberately NOT registered, and the scheduler
        // work-handler set + ordering below is a real composition contract.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutableActivityTemplateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerWorkQueue));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionInspectionWriter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeActivityExecutionInspectionAccumulator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionHierarchyStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionHierarchyReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityExecutionHierarchyWriter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusLookup));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkResumeResolver));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkResumeDispatcher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBookmarkConsumptionCheckpointService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDurableValueStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IIncidentStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionLivenessStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowHoldStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimePauseDecisionProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeRecoveryScanner));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeExecutionOwnershipContextAccessor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeExecutionOwnershipService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeDomainRetryPolicy));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeVolatileWaitPolicy));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeGeneratorEmissionScheduler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerPauseGate));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ISchedulerStateStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(InMemoryRuntimeCheckpointCommitStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimePostCommitOutboxStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionCommandExecutor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowDrainOrchestrator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkflowDrainOrchestratorOptions));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerDrainer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerDrainPolicy));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointPersistencePolicy));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimePostCommitIntentDispatcher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RuntimeCheckpointCommitter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeInputBindingResolver));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerDrainObserver));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeFaultCapturePolicy));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerPoisonStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionActorProvider));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionPool");
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Workflows.Runtime.Core.Contracts.IStorageDriver");
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Workflows.Runtime.Core.Contracts.IStorageDriverContext");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRuntimeExecutionIdGenerator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowStartDispatcher));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor");
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));

        using var rootProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = rootProvider.CreateScope();
        var provider = scope.ServiceProvider;

        // Every service the feature is expected to register must resolve (resolvability replaces implementation-type pins).
        provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        provider.GetRequiredService<IExecutableActivityTemplateStore>();
        provider.GetRequiredService<IWorkflowExecutionCommandExecutor>();
        provider.GetRequiredService<IWorkflowDrainOrchestrator>();
        provider.GetRequiredService<WorkflowDrainOrchestratorOptions>();
        provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        provider.GetRequiredService<IWorkflowExecutionStateStore>();
        provider.GetRequiredService<IActivityExecutionStateStore>();
        provider.GetRequiredService<IActivityExecutionInspectionStore>();
        provider.GetRequiredService<IActivityExecutionInspectionWriter>();
        provider.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>();
        provider.GetRequiredService<IActivityExecutionHierarchyStore>();
        provider.GetRequiredService<IActivityExecutionHierarchyReader>();
        provider.GetRequiredService<IActivityExecutionHierarchyWriter>();
        provider.GetRequiredService<IBookmarkStateStore>();
        provider.GetRequiredService<IBookmarkStimulusLookup>();
        provider.GetRequiredService<IBookmarkResumeResolver>();
        provider.GetRequiredService<IBookmarkResumeDispatcher>();
        provider.GetRequiredService<IBookmarkConsumptionCheckpointService>();
        provider.GetRequiredService<IDurableValueStateStore>();
        provider.GetRequiredService<IIncidentStateStore>();
        provider.GetRequiredService<IExecutionLivenessStateStore>();
        provider.GetRequiredService<IWorkflowHoldStateStore>();
        provider.GetRequiredService<IRuntimePauseDecisionProvider>();
        provider.GetRequiredService<IRuntimeRecoveryScanner>();
        provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>();
        provider.GetRequiredService<IRuntimeExecutionOwnershipService>();
        provider.GetRequiredService<IRuntimeDomainRetryPolicy>();
        provider.GetRequiredService<IRuntimeFaultCapturePolicy>();
        provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        provider.GetRequiredService<IRuntimeVolatileWaitPolicy>();
        provider.GetRequiredService<IRuntimeGeneratorEmissionScheduler>();
        provider.GetRequiredService<IWorkflowSchedulerPauseGate>();
        provider.GetRequiredService<ISchedulerStateStore>();
        provider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        provider.GetRequiredService<IWorkflowSchedulerDrainer>();
        provider.GetRequiredService<IWorkflowSchedulerDrainPolicy>();
        provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>();
        provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        provider.GetRequiredService<RuntimeCheckpointCommitter>();
        provider.GetRequiredService<IRuntimePayloadCapturePolicy>();
        provider.GetRequiredService<IRuntimeInputBindingResolver>();
        provider.GetRequiredService<IRuntimeActivityInputMaterializer>();
        provider.GetRequiredService<WorkflowIntrinsicExecutor>();
        provider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        provider.GetRequiredService<IWorkflowStartDispatcher>();
        Assert.Contains(provider.GetServices<IWorkflowSchedulerDrainObserver>(), observer => observer is NoopWorkflowSchedulerDrainObserver);
        Assert.Contains(provider.GetServices<IWorkflowSchedulerDrainObserver>(), observer => observer is BlockingIncidentWorkflowFaultObserver);
        var schedulerWorkHandlers = provider.GetServices<IWorkflowSchedulerWorkHandler>().ToArray();
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler =>
            StringComparer.Ordinal.Equals(handler.Name, WorkflowStartActivitySchedulerWorkHandler.HandlerName));
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCreateBookmarkSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingBookmarkResumeSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is MissingGeneratedEventSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is NoopWorkflowSchedulerWorkHandler);
        Assert.Contains(schedulerWorkHandlers, handler => handler is IFallbackWorkflowSchedulerWorkHandler);
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowStartSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowScheduleActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler =>
                StringComparer.Ordinal.Equals(handler.Name, WorkflowStartActivitySchedulerWorkHandler.HandlerName)));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler =>
                StringComparer.Ordinal.Equals(handler.Name, WorkflowStartActivitySchedulerWorkHandler.HandlerName)) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCompleteActivitySchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCreateBookmarkSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCreateBookmarkSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is WorkflowCheckpointSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingActivityInvocationSchedulerWorkHandler) <
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingBookmarkResumeSchedulerWorkHandler));
        Assert.True(
            Array.FindIndex(schedulerWorkHandlers, handler => handler is MissingBookmarkResumeSchedulerWorkHandler) <
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
    public void RegistersRuntimeFaultCapturePolicyAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeFaultCapturePolicy>(new CustomRuntimeFaultCapturePolicy());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimeFaultCapturePolicy>(provider.GetRequiredService<IRuntimeFaultCapturePolicy>());
    }

    [Fact]
    public void RegistersWorkflowSchedulerPoisonStoreAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerPoisonStore>(new CustomWorkflowSchedulerPoisonStore());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomWorkflowSchedulerPoisonStore>(provider.GetRequiredService<IWorkflowSchedulerPoisonStore>());
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
    public void RegistersWorkflowSchedulerPauseGateAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerPauseGate>(new CustomWorkflowSchedulerPauseGate());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomWorkflowSchedulerPauseGate>(provider.GetRequiredService<IWorkflowSchedulerPauseGate>());
    }

    [Fact]
    public void RegistersWorkflowHoldStateStoreAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowHoldStateStore>(new CustomWorkflowHoldStateStore());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomWorkflowHoldStateStore>(provider.GetRequiredService<IWorkflowHoldStateStore>());
    }

    [Fact]
    public void RegistersBookmarkStimulusLookupAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBookmarkStimulusLookup>(new CustomBookmarkStimulusLookup());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomBookmarkStimulusLookup>(provider.GetRequiredService<IBookmarkStimulusLookup>());
    }

    [Fact]
    public void RegistersBookmarkResumeResolverAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBookmarkResumeResolver>(new CustomBookmarkResumeResolver());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomBookmarkResumeResolver>(provider.GetRequiredService<IBookmarkResumeResolver>());
    }

    [Fact]
    public void RegistersBookmarkResumeDispatcherAsOverridableDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBookmarkResumeDispatcher>(new CustomBookmarkResumeDispatcher());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomBookmarkResumeDispatcher>(provider.GetRequiredService<IBookmarkResumeDispatcher>());
    }

    [Fact]
    public async Task DefaultSchedulerDrainerStopsBeforeDequeuingPausedWorkflowWork()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 15, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowHoldStateStore>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var drainer = provider.GetRequiredService<IWorkflowSchedulerDrainer>();
        await store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", now, "operator", "Paused for maintenance.")]));
        await queue.EnqueueAsync(new RuntimeSchedulerWorkItem(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:command-1",
            enqueuedAt: now,
            recordedAt: now,
            sequence: 1,
            payload: JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
                new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
                "node-start",
                "actexec-1",
                RuntimeStartActivityCommandPayload.ScheduledActivityReason))));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnPause);
        Assert.Equal(0, result.DrainedCount);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Paused, Assert.Single(result.Items).Status);
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId));
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Cancelled, RuntimeCheckpointNames.ActivityCancelled)]
    [InlineData(ActivityExecutionStatus.Recovered, RuntimeCheckpointNames.ActivityRecovered)]
    public async Task RegisteredCheckpointHandlerProjectsTerminalActivityInspection(
        ActivityExecutionStatus status,
        string checkpointName)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var inspectionStore = provider.GetRequiredService<IActivityExecutionInspectionStore>();
        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>().Single(x => x is WorkflowCheckpointSchedulerWorkHandler);
        var terminalState = NewStateForStatus(status);
        await stateStore.SaveAsync(terminalState);

        await handler.HandleAsync(NewCheckpointWorkItem(terminalState.Execution.ActivityExecutionId, checkpointName));

        var projection = await inspectionStore.FindAsync("wf-1", terminalState.Execution.ActivityExecutionId);
        Assert.NotNull(projection);
        Assert.Equal(status, projection.Status);
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsBookmarksIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsSchedulerStateIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 14, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

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
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await durableValueStateStore.FindAsync("wfexec-1", "durable-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsIncidentsIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await incidentStateStore.FindAsync("wfexec-1", "incident-1"));
        Assert.Single(await incidentStateStore.ListBlockingAsync("wfexec-1"));
    }

    [Fact]
    public async Task DefaultCheckpointWriterProjectsExecutionLivenessStateIntoRegisteredStateStore()
    {
        var services = new ServiceCollection();
        var now = new DateTimeOffset(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var operationalStateStore = provider.GetRequiredService<IExecutionLivenessStateStore>();
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
                    new RuntimeStateChange<ExecutionLivenessState>(
                        StateId: "operational-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: new ExecutionLivenessState(
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        var operationalState = await operationalStateStore.FindAsync("wfexec-1", "operational-1");
        Assert.NotNull(operationalState);
        Assert.Equal("worker-1", operationalState.ExecutionLease!.OwnerId);
    }

    private static ActivityExecutionState NewStateForStatus(ActivityExecutionStatus status) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: $"actexec-{status.ToString().ToLowerInvariant()}",
                WorkflowExecutionId: "wf-1",
                ExecutableNodeId: "node-a",
                AuthoredActivityId: "authored-a",
                ActivityType: "Elsa.Test",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "wf-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewCheckpointWorkItem(
        string activityExecutionId,
        string checkpointName = RuntimeCheckpointNames.ActivityRecovered) =>
        new(
            workItemId: "checkpoint-work",
            workflowExecutionId: "wf-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: "envelope-1",
            idempotencyKey: "wf-1:checkpoint:activity-recovered",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(
                new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
                checkpointName,
                [activityExecutionId],
                RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason)),
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

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

    private sealed class CustomRuntimeFaultCapturePolicy : IRuntimeFaultCapturePolicy
    {
        public RuntimeFaultInfo Capture(Exception exception) =>
            new("Custom", "Custom policy owns fault capture.");
    }

    private sealed class CustomWorkflowSchedulerPoisonStore : IWorkflowSchedulerPoisonStore
    {
        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(record);

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeSchedulerPoisonRecord?>(null);

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>>([]);
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

    private sealed class CustomWorkflowSchedulerPauseGate : IWorkflowSchedulerPauseGate
    {
        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            new((SchedulerPauseDecision?)null);
    }

    private sealed class CustomWorkflowHoldStateStore : IWorkflowHoldStateStore
    {
        public ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default) => new(state);

        public ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default) => new((WorkflowHoldState?)null);

        public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new(Array.Empty<WorkflowHoldState>());

        public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default) =>
            new(Array.Empty<WorkflowHoldState>());
    }

    private sealed class CustomBookmarkStimulusLookup : IBookmarkStimulusLookup
    {
        public ValueTask<BookmarkStimulusLookupResult> FindAsync(BookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default) =>
            new(new BookmarkStimulusLookupResult(BookmarkStimulusLookupStatus.NotFound, reason: "custom"));
    }

    private sealed class CustomBookmarkResumeResolver : IBookmarkResumeResolver
    {
        public BookmarkResumeResolution Resolve(BookmarkResumeRequest request) =>
            throw new NotSupportedException("Custom resolver test double.");
    }

    private sealed class CustomBookmarkResumeDispatcher : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default) =>
            new(new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.NotFound,
                request.WorkflowExecutionId,
                reason: "custom"));
    }
}
