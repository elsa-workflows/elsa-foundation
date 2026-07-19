using CShells.Features;
using Elsa.Attention.Core;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Modularity")]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "ModularityAttention",
    DisplayName = "Module Management Attention",
    Description = "Contributes manifest diagnostics and incompatible module dependencies to Attention.",
    DependsOn = new object[] { "AttentionApi", "ModularityApi" })]
public sealed class ModularityAttentionFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAttentionContributor, ModularityAttentionContributor>());
}
