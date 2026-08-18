using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
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
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, ClrActivityActivator>());
        // A distinct type, not the shared generic capability record: TryAddEnumerable de-duplicates by
        // implementation type, so two capabilities of one class silently collapse into whichever composed first.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, ClrActivityConsumerCapability>());

        // Contribute the Event start-trigger's stimulus provider (W7, E3-1) so the publish-time trigger extractor
        // can recognize published Event nodes and index them. Enumerable so other activity features add their own.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, EventTriggerStimulusProvider>());

        // The PublishEvent durable-first send surface (spec 135 D1). The invocation-keyed staging buffer mirrors the
        // DispatchWorkflow stager's layering (registered as both the activity-facing stager and the engine-facing
        // accessor the invoke handler drains); the post-commit handler routes the staged stimulus in StartAndResume
        // mode. Registered additively per the AddRuntimePostCommitIntentHandler pattern (a distinct intent kind).
        services.TryAddSingleton<PublishStimulusStagingBuffer>();
        services.TryAddSingleton<IPublishStimulusStager>(
            serviceProvider => serviceProvider.GetRequiredService<PublishStimulusStagingBuffer>());
        services.TryAddSingleton<IPublishStimulusStagingAccessor>(
            serviceProvider => serviceProvider.GetRequiredService<PublishStimulusStagingBuffer>());
        services.AddRuntimePostCommitIntentHandler<PublishStimulusExecutor>(
            PublishStimulusConstants.PublishStimulusIntentKind,
            new RuntimePostCommitRetryPolicy(maxAttempts: 4, delay: TimeSpan.FromSeconds(1)));
    }
}
