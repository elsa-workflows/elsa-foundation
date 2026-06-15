using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;

namespace Elsa.Modularity.Api.Services;

public sealed class ShellReloader(IShellRegistry shellRegistry, IShell shell) : IShellReloader
{
    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var result = await shellRegistry.ReloadAsync(shell.Descriptor.Name, cancellationToken);
        return result.Error is null ? 1 : 0;
    }
}
