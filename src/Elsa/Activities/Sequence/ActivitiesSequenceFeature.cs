using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Sequence;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesSequence",
    DisplayName = "Activities Sequence",
    Description = "Sequence composite activity and executable-node child slot contracts."
)]
public class ActivitiesSequenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
