using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.ControlFlow;

/// <summary>
/// Bundles the control-flow composite activities (If, Switch, ForEach, For, While, Do, Parallel) for execution.
/// The activity type identities live in their original per-activity namespaces (e.g.
/// <c>Elsa.Activities.If.Activities.If</c>); only the project/folder and the shell feature are consolidated here.
/// </summary>
/// <remarks>
/// <para>
/// <b>ConfigureServices is intentionally empty.</b> Every one of these activities is CLR-activated and takes its
/// collaborators through the constructor, so none of them needs a registration of its own. The feature still earns
/// its place: enabling it is what puts this assembly in front of the activity-type scan, so the control-flow
/// activities become resolvable. Its design counterpart, <c>ActivitiesControlFlowDesign</c>, owns everything that
/// used to be registered here — all seven structure handlers and the Switch draft validator.
/// </para>
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesControlFlowRuntime",
    DisplayName = "Activities Control Flow (Runtime)",
    Description = "Control-flow composite activity execution (If, Switch, ForEach, For, While, Do, Parallel)."
)]
public class ActivitiesControlFlowRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
