using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Scheduling;

/// <summary>
/// Scheduling activities (<see cref="Activities.Delay"/>). Like the other activity features, the activity
/// types are resolved by the runtime's <c>ClrActivityConstructor</c> — no per-type DI registration is
/// required here. The feature depends on <c>WorkflowsRuntimeScheduling</c> so the durable timer store,
/// scheduler, and hosted timer pump are present to make <c>Delay</c> fire.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesScheduling",
    DisplayName = "Activities Scheduling",
    Description = "Scheduling activities such as Delay that suspend durably on a wall-clock deadline.",
    DependsOn = new object[] { "WorkflowsRuntimeScheduling" }
)]
public sealed class ActivitiesSchedulingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
