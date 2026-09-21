using CShells.Features;
using Elsa.Activities.ControlFlow.Loops;
using Elsa.Activities.If.Internal;
using Elsa.Activities.Parallel.Internal;
using Elsa.Activities.Switch.Internal;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
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
        services.AddSingleton<IActivityStructureHandler>(new LoopStructureHandler(LoopKind.ForEach));
        services.AddSingleton<IActivityStructureHandler>(new LoopStructureHandler(LoopKind.For));
        services.AddSingleton<IActivityStructureHandler>(new LoopStructureHandler(LoopKind.While));
        services.AddSingleton<IActivityStructureHandler>(new LoopStructureHandler(LoopKind.Do));
        services.AddSingleton<IActivityStructureHandler, ParallelStructureHandler>();

        // Activity-owned Draft validator (FR-034): surfaces duplicate Switch case match values as a
        // design-time validation error. Does not block saving; the promotion gate blocks publish.
        services.AddScoped<IDraftValidator, SwitchDuplicateCaseValidator>();
    }
}
