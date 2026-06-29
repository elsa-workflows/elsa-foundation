using CShells.Features;
using Elsa.Activities.Switch.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Switch;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesSwitch",
    DisplayName = "Activities Switch",
    Description = "Switch multi-way control-flow composite activity and its per-case/default child slot contracts."
)]
public class ActivitiesSwitchFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, SwitchStructureHandler>();
    }
}
