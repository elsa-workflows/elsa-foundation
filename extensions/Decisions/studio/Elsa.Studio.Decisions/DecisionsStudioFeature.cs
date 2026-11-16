using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Decisions;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Decisions")]
[StudioModule("decisions", "Decisions", "navigation", "routes", "http", "property-editors")]
[ShellFeature(name: "DecisionsStudio", DisplayName = "Decisions Studio module", Description = "Authors reusable decision tables, cases and immutable versions.")]
public class DecisionsStudioFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) { }
}
