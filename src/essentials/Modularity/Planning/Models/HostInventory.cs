using System.Collections.Immutable;

namespace Elsa.Modularity.Planning.Models;

public sealed record InventoryDependency(string Id, bool Optional);

public sealed record InventoryPackage(
    string PackageId,
    string PackageVersion,
    string ManifestDigest);

public sealed record InventoryFeature(
    string FeatureId,
    string Availability,
    ImmutableArray<string>? RuntimeDependencies,
    ImmutableArray<InventoryDependency>? ManifestDependencies,
    string ManifestReadStatus,
    InventoryPackage? Package,
    bool HostBundled,
    string Compatibility,
    string EvidenceSource);

public sealed record HostInventory(
    string InventoryId,
    string TargetId,
    DateTimeOffset ObservedAt,
    string Source,
    ImmutableArray<InventoryFeature> Features);

public sealed record PersistenceEvidence(
    string Status,
    string Provenance,
    ImmutableArray<string> ResourceReferences,
    ImmutableArray<string> UnresolvedReasons);
