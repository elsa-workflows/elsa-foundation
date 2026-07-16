using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Composition.Runtime;

/// <summary>
/// Runtime-side marker feature for workflow composition. Workflow-backed invocation is compiled through
/// the canonical executable contract and no longer contributes a descriptor constructor.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesCompositionRuntime",
    DisplayName = "Activities Composition (Runtime)",
    Description = "Workflow-backed activity composition contracts."
)]
public class ActivitiesCompositionRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) { }
}
