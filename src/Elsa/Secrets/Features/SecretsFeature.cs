using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "Secrets",
    DisplayName = "Secrets",
    Description = "Provides secret metadata, encrypted and configuration-backed secret stores, and runtime secret expression resolution."
)]
public class SecretsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddSecrets();
}
