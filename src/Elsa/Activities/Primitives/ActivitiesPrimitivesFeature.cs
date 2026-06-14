using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Features.Abstractions;
using Elsa.Features.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives;

/// <summary>
/// Primitive activities + the CLR activity constructor (descriptor type
/// <c>Elsa.Primitives.Models.TypeInformation</c>). A runtime feature — references no
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
        services.AddElsaCapability(ElsaCapabilities.ActivitiesPrimitives, "Primitive activities", "ActivitiesPrimitives", "activities", "runtime", "primitives");
        services.AddSingleton<ActivityArgumentBinder>();
        services.AddSingleton<IActivityConstructor, ClrActivityConstructor>();
    }
}
