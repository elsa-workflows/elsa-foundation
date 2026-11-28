using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Primitives;

/// <summary>
/// Primitive activities and their transient CLR activator. A runtime feature with no Design dependency.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesPrimitives",
    DisplayName = "Activities Primitives",
    Description = "Primitive activities and transient CLR activation."
)]
public class ActivitiesPrimitivesFeature : IShellFeature
{
    private static readonly RuntimeHistorySourceSupport PublicationHistory = RuntimeHistorySourceSupport.ForSourceOwner(
        PublishStimulusConstants.PublishStimulusIntentKind, [RuntimeHistorySourceFactKind.StimulusPublicationCommitted]);

    public void ConfigureServices(IServiceCollection services)
    {
        // The CLR activator binds each activity's scope to the partition of the scope it activates in.
        services.AddPersistenceCore();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, ClrActivityActivator>());
        // A distinct type, not the shared generic capability record: TryAddEnumerable de-duplicates by
        // implementation type, so two capabilities of one class silently collapse into whichever composed first.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, ClrActivityConsumerCapability>());

        // Contribute the Event start-trigger's stimulus provider so the publish-time trigger extractor
        // can recognize published Event nodes and index them. Enumerable so other activity features add their own.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, EventTriggerStimulusProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITestRunTriggerDriver, EventTestRunDriver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITestRunWaitDriver, EventTestRunWaitDriver>());

        // Preserve the named Event wire and producer identity. Actor Hosting owns live delivery.
        services.TryAddSingleton<PublishStimulusStagingBuffer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStimulusDeliveryIntentAdapter, PublishStimulusIntentAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, PublishStimulusSchemaGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, PublishStimulusHistoryEnricher>());
        if (!services.Any(descriptor => ReferenceEquals(descriptor.ImplementationInstance, PublicationHistory)))
            services.AddSingleton(PublicationHistory);
        services.TryAddSingleton<IPublishStimulusStager>(
            serviceProvider => serviceProvider.GetRequiredService<PublishStimulusStagingBuffer>());
        services.TryAddSingleton<IPublishStimulusStagingAccessor>(
            serviceProvider => serviceProvider.GetRequiredService<PublishStimulusStagingBuffer>());
        if (!services.Any(item => item.ServiceType == typeof(WorkflowEventSourceRegistration)))
        {
            if (services.Any(item => item.ServiceType == typeof(StandaloneTriggerSourceDescriptor) && item.IsKeyedService &&
                item.KeyedImplementationInstance is StandaloneTriggerSourceDescriptor descriptor && descriptor.SourceFamily == PublishStimulusIntentAdapter.SourceDescriptor.SourceFamily))
                throw new InvalidOperationException("The canonical workflow Event producer ID is already owned by another source.");
            services.AddSingleton(new WorkflowEventSourceRegistration());
            // Reserve the stable producer identity only. No external producer context or ingress port is installed.
            services.AddKeyedSingleton<StandaloneTriggerSourceDescriptor>(typeof(WorkflowEventSourceRegistration), PublishStimulusIntentAdapter.SourceDescriptor);
            services.AddScoped(_ => new StandaloneTriggerSourceCapability(PublishStimulusIntentAdapter.SourceDescriptor, true)
                { WorkflowIntentKind = PublishStimulusConstants.PublishStimulusIntentKind });
        }
    }

    private sealed class WorkflowEventSourceRegistration;
}
