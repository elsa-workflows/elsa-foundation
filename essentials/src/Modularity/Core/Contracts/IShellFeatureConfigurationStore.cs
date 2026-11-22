using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Contracts;

public interface IShellFeatureConfigurationStore
{
    Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task<ShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureConfigurationChange> features,
        CancellationToken cancellationToken = default);
}
