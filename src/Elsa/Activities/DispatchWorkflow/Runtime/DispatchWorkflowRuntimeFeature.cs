using CShells.Features;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using CShells.Lifecycle;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesDispatchWorkflowRuntime",
    DisplayName = "Activities Dispatch Workflow (Runtime)",
    Description = "Runs pinned DispatchWorkflow child starts through the runtime resumption path.",
    DependsOn = new object[] { "WorkflowsRuntimeResumption" })]
public class DispatchWorkflowRuntimeFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Maximum nesting depth",
        Description = "Maximum number of cross-workflow dispatch edges permitted from a root execution.",
        Category = "Runtime",
        DefaultValue = "32")]
    public int MaxNestingDepth { get; set; } = DispatchWorkflowOptions.DefaultMaxNestingDepth;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        DispatchWorkflowOptions.ValidateMaxNestingDepth(MaxNestingDepth, nameof(MaxNestingDepth));
        services.Configure<DispatchWorkflowOptions>(options => options.MaxNestingDepth = MaxNestingDepth);
        services.AddRuntimePostCommitIntentHandler<ChildStartExecutor>(DispatchWorkflowConstants.StartChildIntentKind);
        services.AddSingleton<IShellInitializer, WorkflowDispatchReadinessInitializer>();
    }
}
