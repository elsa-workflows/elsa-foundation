using CShells.Features;
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
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddRuntimePostCommitIntentHandler<ChildStartExecutor>(DispatchWorkflowConstants.StartChildIntentKind);
    }
}
