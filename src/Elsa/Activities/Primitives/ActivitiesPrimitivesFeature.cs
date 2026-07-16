using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
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
        services.AddSingleton<IActivityActivator, ClrActivityActivator>();

        // Contribute the Event start-trigger's stimulus provider (W7, E3-1) so the publish-time trigger extractor
        // can recognize published Event nodes and index them. Enumerable so other activity features add their own.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, EventTriggerStimulusProvider>());
    }
}
