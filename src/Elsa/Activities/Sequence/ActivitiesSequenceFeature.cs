using CShells.Features;
using Elsa.Activities.Sequence.Internal;
using Elsa.Features.Abstractions;
using Elsa.Features.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
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
        services.AddElsaCapability(ElsaCapabilities.ActivitiesSequence, "Sequence activity", "ActivitiesSequence", "activities", "composition");
        services.AddSingleton<IActivityStructureHandler, SequenceStructureHandler>();
    }
}
