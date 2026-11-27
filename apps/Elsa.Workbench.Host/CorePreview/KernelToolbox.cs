using Elsa.Activities.Design.Core.Options;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench.CorePreview;

/// <summary>
/// Hides the activities the kernel does not run (<see cref="NativeActivityCatalog"/>) from Studio's toolbox, in a shell
/// that serves on the kernel only. It is registered on the host's container, which each shell copies, so each shell's
/// copy checks that shell's composition (<see cref="WorkflowsRuntimeKernelFeature.IsComposedIn"/>). It runs after the
/// shell's own availability rules, so a configured <c>Exclude:Sets</c> keeps its sets and gains this one. The kernel's
/// feature cannot add it itself: the toolbox's options are a design contract, which a runtime project does not reference.
/// </summary>
internal sealed class KernelToolbox(bool servesOnKernel) : IPostConfigureOptions<ActivityAvailabilityOptions>
{
    internal static void Register(IServiceCollection services) =>
        services.AddSingleton<IPostConfigureOptions<ActivityAvailabilityOptions>>(provider =>
            new KernelToolbox(WorkflowsRuntimeKernelFeature.IsComposedIn(provider)));

    public void PostConfigure(string? name, ActivityAvailabilityOptions options)
    {
        if (!servesOnKernel) return;
        options.Sets[NativeActivityCatalog.KernelUnsupportedSet] = [.. NativeActivityCatalog.Unsupported];
        if (!options.Exclude.Sets.Contains(NativeActivityCatalog.KernelUnsupportedSet, StringComparer.Ordinal))
            options.Exclude.Sets = [.. options.Exclude.Sets, NativeActivityCatalog.KernelUnsupportedSet];
    }
}
