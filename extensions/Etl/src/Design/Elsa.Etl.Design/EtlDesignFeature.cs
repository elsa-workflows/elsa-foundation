using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "EtlDesign", DisplayName = "ETL Design", Description = "Validate and compile finite file and HTTP ETL definitions.")]
public class EtlDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<EtlArtifactCompiler>();
        services.AddScoped<IEtlArtifactCompiler>(provider => provider.GetRequiredService<EtlArtifactCompiler>());
        services.AddScoped<IEtlArtifactCompilerV2>(provider => provider.GetRequiredService<EtlArtifactCompiler>());
    }
}
