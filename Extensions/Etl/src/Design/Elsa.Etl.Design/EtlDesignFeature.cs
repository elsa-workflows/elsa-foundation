using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "EtlDesign", DisplayName = "ETL Design", Description = "Validate and compile finite file ETL definitions.")]
public sealed class EtlDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddScoped<IEtlArtifactCompiler, EtlArtifactCompiler>();
}
