using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.FeatureManagement;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Settings")]
[ManifestFeatureCategory("Features")]
[StudioModule("features", "Feature management", "1.0.10", "navigation", "routes", "http", "setting-editors")]
[ShellFeature(
    name: "FeatureManagement",
    DisplayName = "Feature management module",
    Description = "Contributes the Studio feature-management UI module."
)]
public sealed class FeatureManagementStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
