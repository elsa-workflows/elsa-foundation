using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// Serves a shell's publications on the clean-slate kernel (definitions on the kernel, owner decision 1). After every
/// other feature has registered, it replaces the shell's activation coordinator with <see cref="KernelActivationCoordinator"/>
/// and its trigger extractor with <see cref="KernelTriggerBindingExtractor"/>, which are replacement contracts: one
/// implementation each, put in place with <c>Replace</c>. It drops the old core's HTTP route-table observer, which reads
/// old-core trigger bindings a kernel host does not write, and keeps every other trigger index observer, Studio's
/// workflow library among them.
/// </summary>
/// <remarks>
/// The host composes what the kernel shares across shells and registers each by instance, so every shell resolves the
/// host's own: <see cref="KernelServing"/> and <see cref="IScheduleActors"/>. A shell without them is refused when it is
/// built. Release 1 runs the kernel on one shell, the host's default.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel",
    Description = "Serves this shell's publications on the clean-slate kernel: replaces the activation coordinator and the trigger extractor with the kernel's, and leaves out the old core's HTTP route table. Requires a host that composes the kernel's serving state.",
    DependsOn = new object[] { "WorkflowsPublishing", "WorkflowsRuntimeTriggers" })]
public class WorkflowsRuntimeKernelFeature : IShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsRuntimeKernel";

    /// <summary>The old core's HTTP route-table observer, named so that this project needs no reference to its module.</summary>
    public const string RouteTableObserverTypeName = "Elsa.Workflows.Runtime.Http.Services.RouteTableTriggerIndexObserver";

    public virtual void ConfigureServices(IServiceCollection services) { }

    public virtual void PostConfigureServices(IServiceCollection services)
    {
        RequireShared<KernelServing>(services);
        RequireShared<IScheduleActors>(services);

        services.RemoveAll<IWorkflowTriggerBindingExtractor>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor, KernelTriggerBindingExtractor>();

        foreach (var observer in services.Where(IsRouteTableObserver).ToArray()) services.Remove(observer);

        services.RemoveAll<IWorkflowActivationCoordinator>();
        services.AddScoped<IWorkflowActivationCoordinator>(provider => new KernelActivationCoordinator(
            provider.GetRequiredService<IWorkflowActivationAuthority>(),
            provider.GetRequiredService<IWorkflowActivationSwitch>(),
            provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            provider.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>(),
            provider.GetRequiredService<IWorkflowExecutableStore>(),
            provider.GetRequiredService<IWorkflowTriggerBindingStore>(),
            provider.GetRequiredService<KernelServing>(),
            provider.GetRequiredService<IScheduleActors>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<IRecurringTriggerScheduleStore>(),
            provider.GetService<IOptions<ActivationChannelOptions>>()?.Value,
            provider.GetServices<IWorkflowTriggerIndexObserver>(),
            provider.GetService<ILogger<KernelActivationCoordinator>>()));
    }

    private static bool IsRouteTableObserver(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IWorkflowTriggerIndexObserver) && !descriptor.IsKeyedService &&
        (descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())?.FullName == RouteTableObserverTypeName;

    private static void RequireShared<TService>(IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService))
            throw new InvalidOperationException(
                $"The {FeatureName} feature serves on the kernel the host composes, and this host composes no {typeof(TService).Name}. " +
                "Enable the host's kernel composition (on the Workbench, Elsa:CorePreview:Publications), or leave this feature out of the shell.");
    }
}
