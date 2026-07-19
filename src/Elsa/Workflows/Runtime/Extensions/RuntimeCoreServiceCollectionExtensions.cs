using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Core.Extensions;

/// <summary>
/// Host-agnostic composition root for the workflow runtime (ADR 0029 / RT-4). Registers the runtime execution spine —
/// stores, scheduler, drainer, coordinator, command processor, the workflow/activity execution pipelines, and every
/// scheduler work handler — independently of any HTTP/API host, so a worker, a test harness, or another module can
/// compose and drive the runtime without the FastEndpoints API feature. The API feature (<c>WorkflowsRuntimeApiFeature</c>)
/// layers only its endpoint/request-handler wiring on top of this.
/// </summary>
/// <remarks>
/// In-memory state and pure policies are application-wide singletons. The operation graph over persistence ports is
/// scoped, so durable adapters can safely bind storage and access context to one execution. In-process actor mailboxes
/// remain application-wide and cross that lifetime seam through <see cref="ScopedWorkflowExecutionCommandExecutor"/>,
/// which opens a fresh scope for every mailbox command.
/// </remarks>
public static class RuntimeCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the host-agnostic workflow runtime composition root. Idempotent (all registrations use <c>TryAdd</c>),
    /// so a host may register durable-provider overrides before or after calling this.
    /// </summary>
    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ClaimWorkflowTestScopeProvider(typeof(InMemoryWorkflowTestScopeStore), isInMemoryDefault: true);
        services.AddPersistenceCore();
        services.TryAddScoped<IWorkflowExecutionPartitionAccessor, PersistenceWorkflowExecutionPartitionAccessor>();

        // Scheduler work handlers take TimeProvider via constructor injection; register it so GetServices<IWorkflowSchedulerWorkHandler>() can activate them.
        services.TryAddSingleton(TimeProvider.System);

        // MS-9 self-instrumentation: the engine hot path (drain/dispatch/activity-execute/checkpoint-commit) resolves an
        // IWorkflowEngineTracer. The default is a no-op that returns null spans at zero cost, so the fenced drain/commit
        // path is byte-for-byte unchanged unless WorkflowsRuntimeTracingFeature replaces it with the ActivitySource-backed
        // tracer (which itself stays free until a listener attaches).
        services.TryAddSingleton<IWorkflowEngineTracer>(NullWorkflowEngineTracer.Instance);

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IExecutableActivityTemplateStore, InMemoryExecutableActivityTemplateStore>();
        services.TryAddSingleton<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        services.AddOptions<ActivityExecutionHierarchyCursorOptions>();
        services.TryAddSingleton<IActivityExecutionHierarchyCursorCodec, HmacActivityExecutionHierarchyCursorCodec>();
        services.TryAddSingleton<IActivityExecutionHierarchyStore, RuntimeInMemoryActivityExecutionHierarchyStore>();
        services.TryAddSingleton<IActivityExecutionHierarchyReader>(serviceProvider => serviceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
        services.TryAddSingleton<IActivityExecutionHierarchyWriter>(serviceProvider => serviceProvider.GetRequiredService<IActivityExecutionHierarchyStore>());
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<InMemoryActivityExecutionInspectionStore>();
        services.TryAddSingleton<IActivityExecutionInspectionStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        services.TryAddSingleton<IActivityExecutionInspectionWriter>(serviceProvider => serviceProvider.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        services.TryAddScoped<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddScoped<IBookmarkStimulusLookup, BookmarkStimulusLookup>();
        services.TryAddSingleton<IBookmarkResumeResolver, BookmarkResumeResolver>();
        services.TryAddScoped<IBookmarkResumeDispatcher, BookmarkResumeDispatcher>();
        services.TryAddScoped<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
        // Fan-in notifier over IBookmarkLifecycleObserver contributions (spec 089 D). Singleton is safe because the
        // observers themselves register as singletons that open their own scopes (mirroring RouteTableTriggerIndexObserver).
        services.TryAddSingleton<BookmarkLifecycleNotifier>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeDurableValueStorageDriver, JsonRuntimeDurableValueStorageDriver>());
        services.TryAddSingleton<IRuntimeDurableValueStorageDriverRegistry, RuntimeDurableValueStorageDriverRegistry>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();
        services.TryAddSingleton<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>();
        services.TryAddScoped<IRuntimePauseDecisionProvider, RuntimePauseDecisionProvider>();
        services.TryAddScoped<IRuntimeRecoveryScanner, InMemoryRuntimeRecoveryScanner>();
        services.TryAddSingleton<IRuntimeDomainRetryPolicy, NoopRuntimeDomainRetryPolicy>();
        services.TryAddScoped<ActivityActivationFailureHandler>();
        services.AddOptions<RuntimeFaultCaptureOptions>();
        services.TryAddSingleton<IRuntimeFaultCapturePolicy, DefaultRuntimeFaultCapturePolicy>();
        services.TryAddSingleton<IWorkflowSchedulerPoisonStore, InMemoryWorkflowSchedulerPoisonStore>();
        services.TryAddSingleton<IRuntimeVolatileWaitPolicy, DefaultRuntimeVolatileWaitPolicy>();
        services.TryAddScoped<IRuntimeGeneratorEmissionScheduler, RuntimeGeneratorEmissionScheduler>();
        services.TryAddScoped<IWorkflowSchedulerPauseGate, WorkflowSchedulerPauseGate>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<RuntimeExecutionOwnershipOptions>();
        services.TryAddSingleton<IRuntimeExecutionOwnershipContextAccessor, AsyncLocalRuntimeExecutionOwnershipContextAccessor>();
        services.TryAddScoped<IRuntimeExecutionOwnershipService>(serviceProvider =>
            new RuntimeExecutionOwnershipService(
                serviceProvider.GetRequiredService<IExecutionLivenessStateStore>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<RuntimeExecutionOwnershipOptions>()));
        services.TryAddSingleton<InMemoryRuntimeCheckpointStoreState>();
        services.TryAddSingleton<InMemoryWorkflowTestScopeStore>();
        services.TryAddSingleton<IWorkflowTestScopeStore>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryWorkflowTestScopeStore>());
        services.TryAddSingleton<IWorkflowTestScopeAdmissionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryWorkflowTestScopeStore>());
        services.TryAddSingleton<IWorkflowTestScopeCleanupStore>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryWorkflowTestScopeStore>());
        services.TryAddSingleton<IWorkflowDispatchStore, InMemoryWorkflowDispatchStore>();
        services.TryAddSingleton<IWorkflowDispatchQueryStore>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowDispatchStore>() as IWorkflowDispatchQueryStore ??
            throw new InvalidOperationException("The configured workflow dispatch store does not provide bounded query support."));
        services.TryAddSingleton<IWorkflowDispatchDeleteStore>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowDispatchStore>() as IWorkflowDispatchDeleteStore ??
            throw new InvalidOperationException("The configured workflow dispatch store does not provide dispatch deletion support."));
        services.TryAddSingleton<IWorkflowDispatchRetentionRootStore>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowDispatchStore>() as IWorkflowDispatchRetentionRootStore ??
            throw new InvalidOperationException("The configured workflow dispatch store does not provide executable retention-root projection."));
        services.TryAddSingleton<IWorkflowDispatchAdmissionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowDispatchStore>() as IWorkflowDispatchAdmissionStore ??
            throw new InvalidOperationException("The configured workflow dispatch store does not provide atomic child admission."));
        services.TryAddSingleton<IWorkflowDispatchCancellationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<IWorkflowDispatchStore>() as IWorkflowDispatchCancellationStore ??
            throw new InvalidOperationException("The configured workflow dispatch store does not provide atomic parent cancellation resolution."));
        services.TryAddScoped<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddScoped<IRuntimeCheckpointCommitStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddScoped<IRuntimePostCommitOutboxStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddScoped<IPostCommitOutboxLookupStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddScoped<IRuntimePostCommitOutboxClaimStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddScoped<IRuntimePostCommitOutboxClaimCompletionStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddScoped<IWorkflowDispatchRedriveStore, ScopedWorkflowDispatchRedriveStore>();
        services.TryAddScoped<IRuntimePostCommitOutboxProcessor>(serviceProvider =>
            new RuntimePostCommitOutboxProcessor(
                serviceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
                serviceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IRuntimeFaultCapturePolicy>(),
                serviceProvider.GetService<IWorkflowDispatchStore>(),
                serviceProvider.GetServices<IPostCommitFailureProjector>(),
                serviceProvider.GetService<ILogger<RuntimePostCommitOutboxProcessor>>()));
        services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();
        services.TryAddSingleton<RuntimeSchedulerWorkClaimOptions>();
        services.TryAddSingleton<IDurableTimerStore, InMemoryDurableTimerStore>();
        services.TryAddScoped<IActivityScopeCleanupStore, ActivityScopeCleanupStore>();
        services.TryAddSingleton<WorkflowDrainOrchestratorOptions>();
        services.TryAddScoped<IWorkflowDrainOrchestrator, WorkflowDrainOrchestrator>();
        services.TryAddScoped<WorkflowSchedulerCommandRouter>();
        services.TryAddSingleton<IWorkflowExecutionCommandExecutor, ScopedWorkflowExecutionCommandExecutor>();
        services.TryAddSingleton<InMemoryRuntimeDiagnosticsSettingsStore>();
        services.TryAddSingleton<IRuntimeDiagnosticsSettingsStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeDiagnosticsSettingsStore>());
        services.TryAddSingleton<IRuntimeDiagnosticsSettingsAccessor>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeDiagnosticsSettingsStore>());

        // Runtime execution pipeline spine (ADR 0029). The built-in placeholder middleware are registered so the
        // executor can resolve them by type from the built plan; the pass-through slots keep dispatch behavior-preserving
        // until modules register real middleware or a Move 2 slice makes a slot real.
        services.TryAddScoped<RuntimeWorkflowLoadStateMiddleware>();
        services.TryAddScoped<RuntimeWorkflowInvokeMiddleware>();
        services.TryAddScoped<RuntimeWorkflowSchedulingMiddleware>();
        services.TryAddScoped<RuntimeWorkflowCheckpointMiddleware>();
        services.TryAddScoped<RuntimeWorkflowPostCommitMiddleware>();
        services.TryAddScoped<RuntimeActivityLoadStateMiddleware>();
        services.TryAddScoped<RuntimeActivityInputEvaluationMiddleware>();
        services.TryAddScoped<RuntimeActivityInvokeMiddleware>();
        services.TryAddScoped<RuntimeActivityOutputCaptureMiddleware>();
        services.TryAddScoped<RuntimeActivitySchedulingMiddleware>();
        services.TryAddScoped<RuntimeActivityCheckpointMiddleware>();
        services.TryAddScoped<RuntimeActivityPostCommitMiddleware>();
        services.TryAddScoped<IRuntimeWorkflowExecutionPipeline>(serviceProvider =>
            new RuntimeWorkflowExecutionPipeline(
                ComposePlan(
                    serviceProvider,
                    new WorkflowRuntimePipelineBuilder(),
                    serviceProvider.GetServices<WorkflowRuntimeMiddlewareContribution>()
                        .Select(contribution => (contribution.MiddlewareType, contribution.Slot, contribution.Order, contribution.Name))),
                serviceProvider));
        services.TryAddScoped<IRuntimeActivityExecutionPipeline>(serviceProvider =>
            new RuntimeActivityExecutionPipeline(
                ComposePlan(
                    serviceProvider,
                    new ActivityRuntimePipelineBuilder(),
                    serviceProvider.GetServices<ActivityRuntimeMiddlewareContribution>()
                        .Select(contribution => (contribution.MiddlewareType, contribution.Slot, contribution.Order, contribution.Name))),
                serviceProvider));
        services.TryAddScoped<IRuntimeSchedulerPipelineSelector, RuntimeSchedulerPipelineSelector>();
        services.TryAddScoped<IRuntimeExecutionPipelineDispatcher, RuntimeExecutionPipelineDispatcher>();

        services.TryAddScoped<IWorkflowSchedulerDrainer>(serviceProvider =>
            new WorkflowSchedulerDrainer(
                serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
                serviceProvider.GetServices<IWorkflowSchedulerWorkHandler>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IWorkflowSchedulerPauseGate>(),
                serviceProvider.GetRequiredService<IRuntimeExecutionPipelineDispatcher>(),
                serviceProvider.GetRequiredService<IRuntimeFaultCapturePolicy>(),
                serviceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>(),
                serviceProvider.GetRequiredService<IRuntimeDomainRetryPolicy>(),
                serviceProvider.GetRequiredService<IWorkflowEngineTracer>(),
                serviceProvider.GetRequiredService<RuntimeSchedulerWorkClaimOptions>()));
        services.TryAddSingleton<IWorkflowSchedulerDrainPolicy, ImmediateWorkflowSchedulerDrainPolicy>();
        services.TryAddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.TryAddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        services.AddRuntimePostCommitIntentHandler<RuntimeSchedulerPostCommitIntentDispatcher>(RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        services.TryAddScoped<RuntimeCheckpointCommitter>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, WorkflowDispatchCheckpointEnricher>());
        services.TryAddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
        services.TryAddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
        services.TryAddSingleton<IWorkflowExecutableStartPolicy, AllowWorkflowExecutableStartPolicy>();
        services.TryAddSingleton<IRuntimeValueConversionExecutor, RuntimeValueConversionExecutor>();
        services.TryAddScoped<IWorkflowOutputSource, WorkflowOutputSource>();
        services.TryAddSingleton<IRuntimeInputBindingResolver, RuntimeInputBindingResolver>();
        services.TryAddScoped<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
        services.TryAddScoped<WorkflowIntrinsicExecutor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainObserver, NoopWorkflowSchedulerDrainObserver>());
        // Registered before BlockingIncidentWorkflowFaultObserver: the blocking incident it projects from a parked
        // poison record must be visible to the fault observer within the same drain notification pass.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerDrainObserver, PoisonedSchedulerWorkIncidentObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerDrainObserver, BlockingIncidentWorkflowFaultObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowStartSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowScheduleActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowStartActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowCompleteActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowCreateBookmarkSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowCheckpointSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowCancelSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowCancelActivityScopeSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, WorkflowRetryActivityBoundarySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, MissingActivityInvocationSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, MissingBookmarkResumeSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, MissingGeneratedEventSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSchedulerWorkHandler, NoopWorkflowSchedulerWorkHandler>());
        services.TryAddSingleton<IWorkflowExecutionActorProvider, InProcessWorkflowExecutionActorProvider>();
        services.TryAddSingleton<IRuntimeExecutionIdGenerator, ShortRuntimeExecutionIdGenerator>();
        services.TryAddScoped<IWorkflowStartDispatcher>(serviceProvider =>
        {
            var policies = serviceProvider.GetServices<IWorkflowExecutableStartPolicy>().ToArray();
            if (policies.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Runtime composition requires exactly one {nameof(IWorkflowExecutableStartPolicy)} registration, but found {policies.Length}.");
            }

            return new WorkflowStartDispatcher(
                serviceProvider.GetRequiredService<IWorkflowExecutableStore>(),
                serviceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionActorProvider>(),
                serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetService<IWorkflowExecutionPartitionAccessor>(),
                policies[0],
                serviceProvider.GetService<IWorkflowDispatchStore>(),
                serviceProvider.GetService<IWorkflowExecutionStateStore>());
        });
        services.TryAddScoped<IWorkflowDispatchRetentionCollector, WorkflowDispatchRetentionCollector>();
        services.TryAddSingleton<WorkflowDispatchRetentionCursor>();
        services.TryAddScoped<IWorkflowDispatchReadinessAssessor, WorkflowDispatchReadinessAssessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, ProcessLocalCheckpointEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, ProcessLocalDispatchStoreEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, ProcessLocalOutboxEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, ProcessLocalSchedulerEvidence>());

        // Reference GC (ADR 0040): retained execution roots and creation grace are runtime safety policy. A host may
        // configure the policy and schedule the collector through the separate sweeper feature.
        services.AddOptions<WorkflowExecutableGarbageCollectionOptions>();
        services.TryAddScoped<IWorkflowExecutableRootWriteLeaseManager, WorkflowExecutableRootWriteLeaseManager>();
        services.TryAddScoped<IWorkflowExecutableReferenceGarbageCollector, WorkflowExecutableReferenceGarbageCollector>();

        return services;
    }

    // Applies the DI-collected module contributions to the (built-in-seeded) builder, builds the plan, and logs it.
    // Shared by both pipeline kinds so the contribution-application contract lives in one place.
    private static RuntimePipelinePlan ComposePlan(
        IServiceProvider serviceProvider,
        RuntimePipelinePlanBuilder builder,
        IEnumerable<(Type MiddlewareType, string Slot, int Order, string? Name)> contributions)
    {
        foreach (var contribution in contributions)
            builder.Use(contribution.MiddlewareType, contribution.Slot, contribution.Order, contribution.Name);

        var plan = builder.BuildPlan();
        LogResolvedPipeline(serviceProvider, plan);
        return plan;
    }

    // Declarative slot ordering means the fully-resolved pipeline can be inspected instead of guessed. Dump it at Debug
    // once per pipeline (on first resolution) so a contributor can see the final order and source of each middleware.
    private static void LogResolvedPipeline(IServiceProvider serviceProvider, RuntimePipelinePlan plan)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("Elsa.Workflows.Runtime.ExecutionPipeline");
        if (logger is null || !logger.IsEnabled(LogLevel.Debug))
            return;

        var steps = string.Join(Environment.NewLine, plan.Steps.Select((step, index) =>
            $"  [{index}] slot={step.Slot.Name}(#{step.Slot.SortOrder}) order={step.Order} {(step.IsBuiltIn ? "built-in" : "module")} {step.MiddlewareType.FullName}"));

        logger.LogDebug(
            "Resolved {PipelineKind} runtime execution pipeline with {Count} middleware:{NewLine}{Steps}",
            plan.PipelineKind, plan.Steps.Count, Environment.NewLine, steps);
    }
}
