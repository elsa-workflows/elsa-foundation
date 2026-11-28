using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.KernelApi;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Runs published workflows on Actor Foundation and mounts HTTP ingress when the shell selects KernelActivitiesHttp.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel Hosting with Optional HTTP",
    Description = "Runs published Native and Flowchart workflows on Actor Foundation. Select the Kernel HTTP or scheduling activities to serve their triggers.",
    DependsOn = new object[] { WorkflowsRuntimeKernelRetainedApiFeature.FeatureName, "JavaScriptJintEngine", "Tasks" })]
public sealed class WorkflowsRuntimeKernelOptionalHttpHostingFeature : WorkflowsRuntimeKernelHostingFeature
{
    public new const string FeatureName = "WorkflowsRuntimeKernelOptionalHttpHosting";

    internal override string SelectedFeatureName => FeatureName;
    internal override bool RequiresHttp => false;
    internal override bool RequiresScheduleActivities => true;
}
