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
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.ControlFlow.Design;

/// <summary>
/// Registers the design-side structure handlers for the control-flow composites, plus the Switch draft validator.
/// Split out of <c>ActivitiesControlFlowRuntime</c> so a runtime-only engine composes the activities without
/// reaching <c>Elsa.Workflows.Design.Core</c>.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesControlFlowDesign",
    DisplayName = "Activities Control Flow (Design)",
    Description = "Authored structure projection and compilation for the control-flow composites, and duplicate-case Switch validation.",
    DependsOn = new object[] { "ActivitiesControlFlowRuntime" })]
public class ActivitiesControlFlowDesignFeature : IShellFeature
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

        // Activity-owned Draft validator (FR-034): surfaces duplicate Switch case match values as a
        // design-time validation error. Does not block saving; the promotion gate blocks publish.
        services.AddScoped<IDraftValidator, SwitchDuplicateCaseValidator>();
    }
}
