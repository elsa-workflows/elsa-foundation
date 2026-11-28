using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Contributes the kernel's unavailable activity types to a shell's authoring catalog.</summary>
public static class KernelToolbox
{
    public static void Register(IServiceCollection services) =>
        services.AddSingleton(provider => new RuntimeActivityExclusion(NativeActivityCatalog.KernelUnsupportedSet, UnavailableTypes(provider)));

    private static IReadOnlyCollection<string> UnavailableTypes(IServiceProvider provider)
    {
        if (!WorkflowsRuntimeKernelFeature.IsComposedIn(provider))
            return [];

        var capabilities = provider.GetRequiredService<KernelServing>().Capabilities;
        var unavailable = NativeActivityCatalog.Supported.Where(activity =>
            (!capabilities.HttpEndpoints && activity.Value == typeof(HttpEndpoint)) ||
            (!capabilities.Schedules && (activity.Value == typeof(Kernel.Native.Timer) || activity.Value == typeof(Cron))));

        return [.. NativeActivityCatalog.Unsupported, .. unavailable.Select(activity => activity.Key)];
    }
}
