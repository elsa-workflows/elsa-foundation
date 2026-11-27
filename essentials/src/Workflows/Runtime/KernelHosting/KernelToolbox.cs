using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Contributes the kernel's unavailable activity types to a shell's authoring catalog.</summary>
public static class KernelToolbox
{
    public static void Register(IServiceCollection services) =>
        services.AddSingleton(provider => new RuntimeActivityExclusion(NativeActivityCatalog.KernelUnsupportedSet,
            WorkflowsRuntimeKernelFeature.IsComposedIn(provider) ? NativeActivityCatalog.Unsupported : []));
}
