using Nuplane.Abstractions;

namespace Elsa.Server.Nuplane.Endpoints.Models;

internal sealed record PackageCatalogResponse(
    DateTimeOffset SnapshotAtUtc,
    DateTimeOffset PersistedAtUtc,
    IReadOnlyList<ActivePackage> Packages,
    string CorrelationId)
{
    public PackageCatalogResponse(ActivePackagesSnapshot snapshot)
        : this(snapshot.SnapshotAtUtc, snapshot.PersistedAtUtc, snapshot.Packages, snapshot.CorrelationId)
    {
    }
}
