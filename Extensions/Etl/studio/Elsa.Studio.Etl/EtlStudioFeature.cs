using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Etl;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Etl")]
[StudioModule("etl", "ETL pipelines", "navigation", "routes", "http", "property-editors")]
[ShellFeature(name: "EtlStudio", DisplayName = "ETL Studio module",
    Description = "Authors finite ETL pipelines, selects published versions and inspects durable runs.")]
public sealed class EtlStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
