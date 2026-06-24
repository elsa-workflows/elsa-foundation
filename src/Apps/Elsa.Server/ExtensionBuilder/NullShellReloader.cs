using Elsa.Modularity.Core.Contracts;

namespace Elsa.Server.ExtensionBuilder;

internal sealed class NullShellReloader : IShellReloader
{
    public Task<int> ReloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
