using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("AI")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "WeaverWorkflowsStudio",
    DisplayName = "Weaver workflows Studio module",
    Description = "Contributes workflow-aware Weaver context, prompt actions, tools, and proposal review surfaces."
)]
public sealed class WeaverWorkflowsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWeaverWorkflowsStudio();
    }
}
