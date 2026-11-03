using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Modularity.Api.Services;

/// <summary>Reloads the current shell after a feature apply.</summary>
/// <remarks>
/// CShells reports a failed reload (composition, provider build or an initializer threw) as <see cref="ReloadResult.Error"/>
/// rather than throwing, and the previous generation keeps serving. The count stays 0 for that outcome, and the error is
/// logged with its cause so an operator, or a qualification run reading the host log, can see why the apply changed
/// nothing; before, it was discarded.
/// </remarks>
public sealed class ShellReloader(IShellRegistry shellRegistry, IShell shell, ILogger<ShellReloader>? logger = null) : IShellReloader
{
    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var name = shell.Descriptor.Name;
        var result = await shellRegistry.ReloadAsync(name, cancellationToken);
        if (result.Error is null) return 1;
        logger?.LogError(result.Error, "Reloading shell {ShellName} failed; the previous generation keeps serving and the applied features take effect only after a successful reload.", name);
        return 0;
    }
}
