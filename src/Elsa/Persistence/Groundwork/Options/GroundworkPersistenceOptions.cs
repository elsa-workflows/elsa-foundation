using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Options;

public sealed class GroundworkPersistenceOptions
{
    public IList<StorageManifest> Manifests { get; } = [];
    public bool MaterializeOnStartup { get; set; } = true;
}
