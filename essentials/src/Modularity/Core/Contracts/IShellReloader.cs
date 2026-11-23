namespace Elsa.Modularity.Core.Contracts;

public interface IShellReloader
{
    Task<int> ReloadAsync(CancellationToken cancellationToken = default);
}
