using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Primitives;

/// <summary>
/// Primitive activities + the CLR activity constructor (descriptor type
/// <c>Elsa.Primitives.Models.ClrActivityDescriptor</c>). A runtime feature — references no
/// <c>Elsa.*.Design.*</c> project (Elsa §E2.2). Contributes its constructor to the runtime
/// constructor registry via DI; the runtime feature's startup task aggregates it.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesPrimitives",
    DisplayName = "Activities Primitives",
    Description = "Primitive activities and the CLR activity constructor."
)]
public class ActivitiesPrimitivesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ActivityArgumentBinder>();
        services.AddSingleton<ActivityInputHydrator>();
        services.AddSingleton<IActivityActivator, ClrActivityActivator>();
        services.AddSingleton<IActivityConstructor, ClrActivityConstructor>();

        // Contribute the Event start-trigger's stimulus provider (W7, E3-1) so the publish-time trigger extractor
        // can recognize published Event nodes and index them. Enumerable so other activity features add their own.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, EventTriggerStimulusProvider>());
    }
}
