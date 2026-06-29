using CShells.Features;
using Elsa.Activities.Do.Internal;
using Elsa.Activities.For.Internal;
using Elsa.Activities.ForEach.Internal;
using Elsa.Activities.If.Internal;
using Elsa.Activities.Parallel.Internal;
using Elsa.Activities.Switch.Internal;
using Elsa.Activities.While.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.ControlFlow;

/// <summary>
/// Bundles the control-flow composite activities (If, Switch, ForEach, For, While, Do, Parallel) and
/// registers their design-side structure handlers. The activity type identities live in their original
/// per-activity namespaces (e.g. <c>Elsa.Activities.If.Activities.If</c>); only the project/folder and the
/// shell feature are consolidated here.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesControlFlow",
    DisplayName = "Activities Control Flow",
    Description = "Control-flow composite activities (If, Switch, ForEach, For, While, Do, Parallel) and their child slot contracts."
)]
public class ActivitiesControlFlowFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, IfStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, SwitchStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, ForEachStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, ForStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, WhileStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, DoStructureHandler>();
        services.AddSingleton<IActivityStructureHandler, ParallelStructureHandler>();
    }
}
