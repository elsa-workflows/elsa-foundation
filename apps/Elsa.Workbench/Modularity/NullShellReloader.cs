using Elsa.Modularity.Core.Contracts;

namespace Elsa.Workbench;

internal sealed class NullShellReloader : IShellReloader
{
    public Task<int> ReloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
