using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Providers;

public interface IGroundworkPersistenceProvider
{
    ProviderIdentity Identity { get; }
    Task MaterializeAsync(StorageManifest manifest, CancellationToken cancellationToken = default);
}
