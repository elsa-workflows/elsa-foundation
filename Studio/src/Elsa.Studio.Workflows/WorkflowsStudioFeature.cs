using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "WorkflowsStudio",
    DisplayName = "Workflows Studio module",
    Description = "Contributes workflow definition management and design routes to Elsa Studio."
)]
public sealed class WorkflowsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddWorkflowsStudio();
    }
}
