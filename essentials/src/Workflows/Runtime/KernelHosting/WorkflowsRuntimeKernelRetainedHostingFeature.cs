using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.TryAddScoped<IWorkflowExecutionPartitionAccessor, PersistenceWorkflowExecutionPartitionAccessor>();
        services.TryAddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
        services.TryAddSingleton<IRuntimeExecutionIdGenerator, RuntimeExecutionIdGenerator>();
        services.AddWorkflowTestRunTransport();
        services.TryAddScoped<IWorkflowStartDispatcher>(provider => provider.GetRequiredService<IWorkflowDirectTestRunDispatcher>());
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(KernelShellStorage)) != 1)
            throw new InvalidOperationException("A shell must select exactly one Actor Foundation hosting feature.");
    }
}
