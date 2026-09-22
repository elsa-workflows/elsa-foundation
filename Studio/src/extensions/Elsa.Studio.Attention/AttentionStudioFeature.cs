using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Attention")]
[StudioModule("attention", "Attention", "1.0.0", "dashboard-widgets", "http")]
[ShellFeature(name: "AttentionStudio", DisplayName = "Attention", Description = "Presents attention items in Studio.")]
public sealed class AttentionStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
