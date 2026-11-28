using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime;

/// <summary>
/// Runtime-side feature for durable activity invocation, completion projection, and CLR type discovery.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesRuntime",
    DisplayName = "Activities Runtime",
    Description = "Typed activity invocation and completion runtime."
)]
public class ActivitiesRuntimeFeature : Elsa.Events.EventsFeature
{
    private static readonly RuntimeHistorySourceSupport ContinuationSources = new([
        RuntimeHistorySourceFactKind.ActivityContinuationStarted, RuntimeHistorySourceFactKind.ActivityContinuationReturned,
        RuntimeHistorySourceFactKind.ActivityContinuationSuspended, RuntimeHistorySourceFactKind.ActivityContinuationCompleted,
        RuntimeHistorySourceFactKind.ActivityContinuationFaulted, RuntimeHistorySourceFactKind.ActivityContinuationCancelled,
        RuntimeHistorySourceFactKind.ChildControlAccepted, RuntimeHistorySourceFactKind.ChildControlApplied]);

    public override void ConfigureServices(IServiceCollection services)
    {
        // Resume validators use the canonical Events pipeline in composed and direct library hosts alike.
        base.ConfigureServices(services);
        new ActivitiesRuntimeHostingFeature().ConfigureServices(services);
        if (!services.Any(descriptor => ReferenceEquals(descriptor.ImplementationInstance, ContinuationSources))) services.AddSingleton(ContinuationSources);
        services.TryAddSingleton<ActivityInputHydrator>();
        services.TryAddScoped<ActivitySecretInputResolver>();
        services.AddOptions<BackgroundActivityWorkerOptions>();
        services.TryAddScoped<BackgroundActivityCompletionCodec>();
        services.TryAddScoped<BackgroundActivityCoordinator>();
        services.TryAddScoped<BackgroundActivityResultApplication>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, BackgroundActivityCheckpointEnricher>());
        services.TryAddScoped<IActivityActivator, ActivityActivator>();
        services.TryAddScoped<IRuntimeActivityInputMaterializer, RuntimeActivityInputMaterializer>();
        services.TryAddSingleton<ActivityCompletionProjector>();
        services.TryAddSingleton<IRuntimeValueConversionExecutor, RuntimeValueConversionExecutor>();
        services.TryAddSingleton<RuntimeOutputCaptureProjector>();
        services.TryAddScoped<ActivityFaultIncidentRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowInvokeActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowParentActivityCompletionSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowNotifyParentActivitySchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowChildControlSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowInterventionDecisionSchedulerWorkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowSchedulerWorkHandler, WorkflowResumeBookmarkSchedulerWorkHandler>());
    }
}
