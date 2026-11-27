using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
public class WorkflowsRuntimeKernelApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernelApi";

    public virtual void ConfigureServices(IServiceCollection services) => services.AddDataProtection();

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        KernelHistoryStream.Map(endpoints);
        KernelParkRecovery.Map(endpoints);
        KernelParkInspection.Map(endpoints);
    }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(KernelRuns) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature needs the host's {nameof(KernelRuns)}, registered by instance on the root container.");
        services.AddScoped<KernelRunReader>();
        services.AddScoped<KernelActivityTimeline>();
        services.AddScoped<KernelHistoryCursor>();
        services.AddScoped<KernelPlaybackCursor>();
        services.AddApiCapabilitySource<KernelHistoryCapabilitySource>();
        services.AddApiCapabilitySource<KernelParkInspectionCapabilitySource>();
        services.TryAddScoped<ActivityExecutionBrowseSource>();
        ReplaceService(typeof(IWorkflowInstanceListService), typeof(WorkflowInstanceListService), typeof(KernelWorkflowInstanceListService));
        ReplaceService(typeof(IWorkflowInstanceDetailsService), typeof(WorkflowInstanceDetailsService), typeof(KernelWorkflowInstanceDetailsService));
        ReplaceService(typeof(IActivityExecutionBrowseSource), typeof(ActivityExecutionBrowseSource), typeof(KernelActivityExecutionBrowseSource));
        ReplaceService(typeof(IActivityExecutionInspectionService), typeof(ActivityExecutionInspectionService), typeof(KernelActivityExecutionInspectionService));
        ReplaceService(typeof(IRuntimeHistoryPageReader), typeof(RuntimeHistoryInspectionService), typeof(KernelRuntimeHistoryPageReader));
        ReplaceService(typeof(IRuntimeHistoryOccurrencePageReader), typeof(RuntimeHistoryInspectionService), typeof(KernelPlaybackPageReader));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(KernelOperatorIngress) && !descriptor.IsKeyedService))
        {
            ReplaceService(typeof(IWorkflowInstanceDeletionService), typeof(WorkflowInstanceDeletionService), typeof(KernelWorkflowInstanceDeletionService));
            services.TryAddSingleton<SelectedRunDeletionAvailable>();
            services.TryAddScoped<KernelParkRecovery>();
            services.AddApiCapabilitySource<KernelParkRecoveryCapabilitySource>();
        }
        ReplaceService(typeof(IWorkflowExecutableRetentionReader), typeof(LegacyWorkflowExecutableRetentionReader),
            typeof(KernelWorkflowExecutableRetentionReader));

        void ReplaceService(Type service, Type runtimeImplementation, Type kernelImplementation)
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
