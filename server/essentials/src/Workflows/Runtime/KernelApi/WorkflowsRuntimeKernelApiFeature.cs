using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>
/// Serves a kernel shell's workflow runtime API run reads from the kernel: after every other feature has registered, it
/// puts <see cref="KernelWorkflowInstanceListService"/> in the place of the runtime API's workflow-instance list, so
/// Studio's Runs page lists the kernel's runs, and only those. It requires the host's <see cref="KernelRuns"/>,
/// registered by instance as the host's <see cref="KernelServing"/> is.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel API",
    Description = "Serves the workflow runtime API's run list from the clean-slate kernel on a kernel shell, so Studio's Runs page lists the kernel's runs. Requires a host that composes the kernel's run reads.",
    DependsOn = new object[] { WorkflowsRuntimeKernelFeature.FeatureName, "WorkflowsRuntimeApi" })]
public class WorkflowsRuntimeKernelApiFeature : IShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernelApi";

    public virtual void ConfigureServices(IServiceCollection services) { }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(KernelRuns) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature lists the host's kernel runs and needs the host's {nameof(KernelRuns)}, registered by instance on the root container.");
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowInstanceListService) && !descriptor.IsKeyedService).ToArray())
        {
            // The runtime API registers its default list, by its type or through a factory of its own assembly that resolves
            // it; anything else is another implementation this feature would replace without a word.
            var runtimeApi = typeof(WorkflowInstanceListService).Assembly;
            if (descriptor.ImplementationType != typeof(WorkflowInstanceListService) && descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly != runtimeApi)
                throw new InvalidOperationException(
                    $"The {FeatureName} feature replaces the shell's {nameof(IWorkflowInstanceListService)} with the kernel's, and this shell registers another for it. " +
                    $"One implementation serves per shell: remove that registration, or leave the {FeatureName} feature out of the shell.");
            services.Remove(descriptor);
        }
        services.AddScoped<IWorkflowInstanceListService, KernelWorkflowInstanceListService>();
    }
}
