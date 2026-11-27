using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>
/// Serves a kernel shell's workflow lists, run summaries and activity timelines from the host's <see cref="KernelRuns"/>.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel API",
    Description = "Serves workflow lists, run summaries and activity timelines from the clean-slate kernel on a kernel shell.",
    DependsOn = new object[] { WorkflowsRuntimeKernelFeature.FeatureName, "WorkflowsRuntimeApi" })]
public class WorkflowsRuntimeKernelApiFeature : IShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernelApi";

    public virtual void ConfigureServices(IServiceCollection services) { }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(KernelRuns) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature needs the host's {nameof(KernelRuns)}, registered by instance on the root container.");
        services.AddScoped<KernelRunReader>();
        services.AddScoped<KernelActivityTimeline>();
        services.TryAddScoped<ActivityExecutionBrowseSource>();
        ReplaceReadService(typeof(IWorkflowInstanceListService), typeof(WorkflowInstanceListService), typeof(KernelWorkflowInstanceListService));
        ReplaceReadService(typeof(IWorkflowInstanceDetailsService), typeof(WorkflowInstanceDetailsService), typeof(KernelWorkflowInstanceDetailsService));
        ReplaceReadService(typeof(IActivityExecutionBrowseSource), typeof(ActivityExecutionBrowseSource), typeof(KernelActivityExecutionBrowseSource));
        ReplaceReadService(typeof(IActivityExecutionInspectionService), typeof(ActivityExecutionInspectionService), typeof(KernelActivityExecutionInspectionService));

        void ReplaceReadService(Type service, Type runtimeImplementation, Type kernelImplementation)
        {
            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == service && !descriptor.IsKeyedService).ToArray())
            {
                // The runtime API registers each default by type or through a factory within its own assembly.
                if (descriptor.ImplementationType != runtimeImplementation && descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly != runtimeImplementation.Assembly)
                    throw new InvalidOperationException(
                        $"The {FeatureName} feature replaces the shell's {service.Name} with the kernel's, and this shell registers another for it. " +
                        $"One implementation serves per shell: remove that registration, or leave the {FeatureName} feature out of the shell.");
                services.Remove(descriptor);
            }
            services.AddScoped(service, kernelImplementation);
        }
    }
}
