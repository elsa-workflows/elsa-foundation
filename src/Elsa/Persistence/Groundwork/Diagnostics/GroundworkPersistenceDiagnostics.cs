using Elsa.Persistence.Groundwork.Options;
using Elsa.Persistence.Groundwork.Providers;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.Groundwork.Diagnostics;

public sealed class GroundworkPersistenceDiagnostics(
    IOptions<GroundworkPersistenceOptions> options,
    IEnumerable<IGroundworkPersistenceProvider> providers,
    GroundworkPersistenceDiagnosticStore store)
{
    public GroundworkPersistenceDiagnosticSnapshot GetSnapshot()
    {
        var manifests = options.Value.Manifests
            .Select(manifest => new GroundworkManifestDiagnostic(
                manifest.Identity.Value,
                manifest.Version.Value,
                manifest.Owner.Value))
            .ToList();
        var providerDiagnostics = providers
            .Select(provider => new GroundworkProviderDiagnostic(provider.Identity.Name, provider.Identity.Version))
            .ToList();

        return new GroundworkPersistenceDiagnosticSnapshot(
            manifests,
            providerDiagnostics,
            store.GetRecords());
    }
}

public sealed class GroundworkPersistenceDiagnosticStore
{
    private readonly object gate = new();
    private readonly List<GroundworkMaterializationDiagnostic> records = [];

    public void Record(GroundworkMaterializationDiagnostic diagnostic)
    {
        lock (gate)
            records.Add(diagnostic);
    }

    public IReadOnlyList<GroundworkMaterializationDiagnostic> GetRecords()
    {
        lock (gate)
            return records.ToList();
    }
}
