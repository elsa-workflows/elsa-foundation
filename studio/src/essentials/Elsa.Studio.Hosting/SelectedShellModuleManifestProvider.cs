using CShells.Lifecycle;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;

namespace Elsa.Studio.Hosting;

/// <summary>Root consumers borrow the selected shell's real scoped provider for the entire asynchronous read.</summary>
internal sealed class SelectedShellModuleManifestProvider(IShellRegistry registry, StudioHostingSettings settings) : IStudioModuleManifestProvider
{
    public async Task<StudioModulesResponse> GetModules(CancellationToken cancellationToken)
    {
        await using var scope = await StudioShellActivation.AcquireScopeAsync(registry, settings.ShellName, cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(cancellationToken);
    }

    public async Task<StudioModuleRegistryResponse> GetModuleRegistry(CancellationToken cancellationToken)
    {
        await using var scope = await StudioShellActivation.AcquireScopeAsync(registry, settings.ShellName, cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(cancellationToken);
    }
}
