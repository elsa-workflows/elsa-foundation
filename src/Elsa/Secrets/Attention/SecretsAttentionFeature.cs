using CShells.Features;
using Elsa.Attention.Core;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Secrets.Attention;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Attention")]
[ShellFeature(
    name: "SecretsAttention",
    DisplayName = "Secrets Attention",
    Description = "Contributes expired, revoked, and soon-expiring secret conditions to Attention.",
    DependsOn = new object[] { "AttentionApi", "Secrets" })]
public sealed class SecretsAttentionFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAttentionContributor, SecretsAttentionContributor>());
}
