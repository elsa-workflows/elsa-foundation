using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Hosts Actor Foundation with retained workflow APIs and the HTTP pipeline for kernel shells.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel Retained Hosting",
    Description = "Runs Native and Flowchart on Actor Foundation with the retained Runtime API and standalone HTTP ingress.",
    DependsOn = new object[] { "WorkflowsRuntimeKernelRetainedApi", "KernelActivitiesHttp", "JavaScriptJintEngine", "Tasks" })]
public sealed class WorkflowsRuntimeKernelRetainedHostingFeature : WorkflowsRuntimeKernelHostingFeature, IPostConfigureShellServices
{
    public new const string FeatureName = "WorkflowsRuntimeKernelRetainedHosting";

    protected override string HostingFeatureName => FeatureName;

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(KernelShellStorage)) != 1)
            throw new InvalidOperationException("A shell must select exactly one Actor Foundation hosting feature.");
    }
}
