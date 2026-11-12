using CShells.Features;
using Elsa.Weaver.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver;

/// <summary>Enables the generic effects contract for a compatible selected store.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ShellFeature("WeaverEffects", DisplayName = "Foundation Weaver Effects",
    Description = "Enables generic privilege declarations, module codes and performed effects for the selected compatible store.",
    DependsOn = new object[] { typeof(WeaverFeature) })]
public class WeaverEffectsFeature : WeaverFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        WeaverEffectsAdmission.Register(services, GetType());
    }
}
