using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsRuntimeApi",
    DisplayName = "Workflows Runtime API",
    Description = "Runtime workflow execution endpoints for published WorkflowExecutable artifacts."
)]
public class WorkflowsRuntimeApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddSingleton<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>();
        services.TryAddSingleton<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        services.TryAddSingleton<InMemoryActivityExecutionInspectionStore>();
        services.TryAddSingleton<IActivityExecutionInspectionStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        services.TryAddSingleton<IActivityExecutionInspectionWriter>(serviceProvider => serviceProvider.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        // Stateless merge helper over runtime stores; singleton avoids captive scopes in singleton scheduler handlers.
        services.TryAddSingleton<IRuntimeActivityExecutionInspectionAccumulator, RuntimeActivityExecutionInspectionAccumulator>();
        services.TryAddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        services.TryAddSingleton<IBookmarkStimulusLookup, BookmarkStimulusLookup>();
        services.TryAddSingleton<IBookmarkResumeResolver, BookmarkResumeResolver>();
        services.TryAddSingleton<IBookmarkResumeDispatcher, BookmarkResumeDispatcher>();
        services.TryAddSingleton<IBookmarkConsumptionCheckpointService, BookmarkConsumptionCheckpointService>();
        services.TryAddSingleton<IDurableValueStateStore, InMemoryDurableValueStateStore>();
        services.TryAddSingleton<IRuntimeActivityOutputRegister, InMemoryRuntimeActivityOutputRegister>();
        services.TryAddSingleton<IIncidentStateStore, InMemoryIncidentStateStore>();
        services.TryAddSingleton<IOperationalStateStore, InMemoryOperationalStateStore>();
        services.TryAddSingleton<IControlPlaneStateStore, InMemoryControlPlaneStateStore>();
        services.TryAddSingleton<IRuntimePauseDecisionProvider, RuntimePauseDecisionProvider>();
        services.TryAddSingleton<IRuntimeRecoveryScanner, InMemoryRuntimeRecoveryScanner>();
        services.TryAddSingleton<IRuntimeDomainRetryPolicy, NoopRuntimeDomainRetryPolicy>();
        services.TryAddSingleton<IRuntimeVolatileWaitPolicy, DefaultRuntimeVolatileWaitPolicy>();
        services.TryAddSingleton<IRuntimeGeneratorEmissionScheduler, RuntimeGeneratorEmissionScheduler>();
        services.TryAddSingleton<IWorkflowSchedulerPauseGate, WorkflowSchedulerPauseGate>();
        services.TryAddSingleton<ISchedulerStateStore, InMemorySchedulerStateStore>();
        services.TryAddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.TryAddSingleton<IRuntimeCheckpointCommitStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.TryAddSingleton<IRuntimePostCommitOutboxProcessor, RuntimePostCommitOutboxProcessor>();
        services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();
        services.TryAddSingleton<IWorkflowExecutionAmbientServicesAccessor, AsyncLocalWorkflowExecutionAmbientServicesAccessor>();
        services.TryAddSingleton<WorkflowExecutionDrainCoordinatorOptions>();
        services.TryAddSingleton<IWorkflowExecutionDrainCoordinator, WorkflowExecutionDrainCoordinator>();
        services.TryAddSingleton<IWorkflowExecutionCommandProcessor, WorkflowSchedulerCommandProcessor>();
        services.TryAddSingleton<IWorkflowSchedulerDrainer>(serviceProvider =>
            new WorkflowSchedulerDrainer(
                serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
                serviceProvider.GetServices<IWorkflowSchedulerWorkHandler>(),
                TimeProvider.System,
                serviceProvider.GetRequiredService<IWorkflowSchedulerPauseGate>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionAmbientServicesAccessor>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>()));
        services.TryAddSingleton<IWorkflowSchedulerDrainPolicy, ImmediateWorkflowSchedulerDrainPolicy>();
        services.TryAddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.TryAddSingleton<IRuntimePostCommitIntentDispatcher, RuntimeSchedulerPostCommitIntentDispatcher>();
        services.TryAddSingleton<RuntimeCheckpointCommitter>();
        services.TryAddSingleton<IRuntimePayloadCapturePolicy, DefaultRuntimePayloadCapturePolicy>();
        services.TryAddSingleton<IRuntimeInputBindingResolver, RuntimeInputBindingResolver>();
        services.TryAddSingleton<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainObserver, NoopWorkflowSchedulerDrainObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowStartSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowScheduleActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowStartActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowCompleteActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowCreateBookmarkSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowCheckpointSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowCancelSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, MissingActivityInvocationSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, MissingBookmarkResumeSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, MissingGeneratedEventSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, NoopWorkflowSchedulerWorkHandler>());
        services.TryAddSingleton<IWorkflowExecutionAgentProvider, InProcessWorkflowExecutionAgentProvider>();
        services.TryAddSingleton<IRuntimeExecutionIdGenerator, GuidRuntimeExecutionIdGenerator>();
        services.TryAddSingleton<IWorkflowExecutionStartDispatcher, WorkflowExecutionStartDispatcher>();
        services.AddRequestHandlersFrom(GetType().Assembly);
    }
}
